using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;
using AutoKiwi.Orchestration;
using AutoKiwi.Minimal;
using AutoKiwi.Translation;

namespace AutoKiwi.Generation
{
    /// <summary>
    /// Multi-Pass generátor kódu podle PDF dokumentu
    /// Implementuje strategii postupného generování kódu ve více krocích
    /// </summary>
    public class MultiPassCodeGenerator : ICodeGenerator
    {
        private readonly IEventBus _eventBus;
        private readonly ILlmClient _llmClient;
        private readonly PromptTranslator _promptTranslator;
        private readonly ContextManager _contextManager;
        private readonly IApplicationMemory _applicationMemory;
        
        // Statistiky úspěšnosti generování
        private int _totalGenerationAttempts = 0;
        private int _successfulGenerations = 0;
        private int _totalRepairAttempts = 0;
        private int _successfulRepairs = 0;
        
        // Konfigurace Multi-Pass generování
        private readonly MultiPassConfig _config;

        /// <summary>
        /// Vytvoří novou instanci MultiPassCodeGenerator
        /// </summary>
        public MultiPassCodeGenerator(
            IEventBus eventBus,
            ILlmClient llmClient,
            PromptTranslator promptTranslator,
            ContextManager contextManager,
            IApplicationMemory applicationMemory,
            MultiPassConfig config = null)
        {
            _eventBus = eventBus;
            _llmClient = llmClient;
            _promptTranslator = promptTranslator;
            _contextManager = contextManager;
            _applicationMemory = applicationMemory;
            _config = config ?? new MultiPassConfig();
        }
        
        /// <summary>
        /// Konfigurace multi-pass generování
        /// </summary>
        public class MultiPassConfig
        {
            /// <summary>
            /// Maximální počet pokusů o opravu kódu v jednom cyklu
            /// </summary>
            public int MaxRepairAttempts { get; set; } = 3;
            
            /// <summary>
            /// Zda používat specializované modely pro různé komponenty
            /// </summary>
            public bool UseSpecializedModels { get; set; } = true;
            
            /// <summary>
            /// Zda automaticky integrovat komponenty
            /// </summary>
            public bool AutoIntegrate { get; set; } = true;
            
            /// <summary>
            /// Úroveň detailu v plánování
            /// </summary>
            public DetailLevel PlanningDetail { get; set; } = DetailLevel.High;
            
            /// <summary>
            /// Minimální velikost pro rozdělení na komponenty
            /// </summary>
            public int MinComponentSplitThreshold { get; set; } = 5;
        }
        
        /// <summary>
        /// Úroveň detailu
        /// </summary>
        public enum DetailLevel
        {
            Low,
            Medium,
            High
        }

        /// <summary>
        /// Generuje kód aplikace ve více krocích podle popisu
        /// </summary>
        public async Task<string> GenerateCodeAsync(
            string applicationDescription,
            string developmentPlan,
            ApplicationType applicationType)
        {
            LogMessage($"Zahajuji multi-pass generování kódu pro aplikaci: {applicationDescription}");
            _totalGenerationAttempts++;
            
            string conversationId = Guid.NewGuid().ToString();
            
            try
            {
                // Publikujeme událost o začátku generování
                _eventBus.Publish(new GenerationStartedEvent(conversationId, applicationDescription, applicationType));
                
                // 0. Předběžná analýza a získání kontextu
                string relevantContext = await _applicationMemory.GetRelevantContext(applicationDescription);
                
                // 1. První pass - Strategické plánování a rozdělení na komponenty
                GenerationPlan generationPlan = await CreateGenerationPlanAsync(
                    applicationDescription,
                    developmentPlan,
                    applicationType,
                    relevantContext,
                    conversationId);
                
                // Publikujeme událost o vytvoření plánu
                _eventBus.Publish(new PlanCreatedEvent(conversationId, generationPlan));
                
                // 2. Druhý pass - Rozdělení na komponenty s přesným plánem implementace
                var components = await AnalyzeAndDivideIntoComponentsAsync(
                    applicationDescription, 
                    generationPlan,
                    conversationId);
                
                LogMessage($"Aplikace rozdělena na {components.Count} komponent");
                
                // 3. Třetí pass - Generování implementačního plánu pro každou komponentu
                await GenerateImplementationPlansAsync(components, generationPlan, conversationId);
                
                // 4. Čtvrtý pass - Generování kódu pro jednotlivé komponenty
                await GenerateComponentCodeAsync(components, generationPlan, conversationId);
                
                // 5. Pátý pass - Odhalení vzájemných závislostí mezi komponenty
                components = await DetectComponentDependenciesAsync(components, conversationId);
                
                // 6. Šestý pass - Integrace komponent s řešením konfliktů
                string integratedCode = await IntegrateComponentsAsync(
                    components, 
                    applicationDescription, 
                    applicationType,
                    generationPlan,
                    conversationId);
                
                // 7. Sedmý pass - Finální kontrola a čištění kódu
                string finalCode = await PolishFinalCodeAsync(
                    integratedCode,
                    applicationDescription,
                    applicationType,
                    conversationId);
                
                _successfulGenerations++;
                LogMessage("Multi-pass generování kódu úspěšně dokončeno");
                
                // Publikujeme událost o dokončení generování
                _eventBus.Publish(new GenerationCompletedEvent(
                    conversationId, 
                    finalCode, 
                    true, 
                    "Generování úspěšně dokončeno"));
                
                return finalCode;
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při multi-pass generování kódu: {ex.Message}", MessageSeverity.Error);
                
                // Publikujeme událost o selhání
                _eventBus.Publish(new GenerationCompletedEvent(
                    conversationId, 
                    null, 
                    false, 
                    $"Chyba při generování: {ex.Message}"));
                
                // Při selhání zkusíme jednoprůchodové generování jako fallback
                LogMessage("Přepínám na jednoprůchodové generování jako fallback", MessageSeverity.Warning);
                return await FallbackSinglePassGenerationAsync(
                    applicationDescription, 
                    developmentPlan, 
                    applicationType);
            }
        }
        
        /// <summary>
        /// Vytváří komplexní plán generování
        /// </summary>
        private async Task<GenerationPlan> CreateGenerationPlanAsync(
            string applicationDescription,
            string developmentPlan,
            ApplicationType applicationType,
            string relevantContext,
            string conversationId)
        {
            LogMessage("Pass 0: Vytváření strategického plánu generování");
            
            // Vytvoření kontextu pro prompt
            var workflowContext = new WorkflowContext
            {
                Description = applicationDescription,
                OperationId = conversationId,
                CurrentStage = WorkflowStage.Planning
            };
            
            // Vytvoření promptu pro strategické plánování
            string planningPrompt = await _promptTranslator.TranslateToModelPromptAsync(
                $"Create a strategic development plan for this application: {applicationDescription}\n\nRequirements:\n{developmentPlan}\n\nBased on similar applications: {relevantContext}",
                "codellama:13b", // Použijeme větší model pro strategické plánování
                PromptType.Planning,
                workflowContext);
            
            // Získání odpovědi od LLM
            string planningResponse = await _llmClient.GenerateAsync(planningPrompt);
            
            // Zaznamenání odpovědi do kontextu
            await _contextManager.AddMessageAsync(
                conversationId,
                new ConversationMessage
                {
                    Role = "assistant",
                    Content = planningResponse,
                    Timestamp = DateTime.Now,
                    MessageType = MessageType.Response
                });
            
            // Parsování plánu z odpovědi
            var plan = ParseGenerationPlan(planningResponse, applicationDescription, applicationType);
            
            LogMessage($"Strategický plán vytvořen: {plan.MainComponents.Count} hlavních komponent, {plan.DependencyGraph.Count} závislostí");
            
            return plan;
        }
        
        /// <summary>
        /// Parsuje plán generování z odpovědi LLM
        /// </summary>
        private GenerationPlan ParseGenerationPlan(
            string planningResponse, 
            string applicationDescription,
            ApplicationType applicationType)
        {
            var plan = new GenerationPlan
            {
                Description = applicationDescription,
                ApplicationType = applicationType,
                MainComponents = new List<string>(),
                ComponentDetails = new Dictionary<string, string>(),
                DependencyGraph = new Dictionary<string, List<string>>(),
                ImplementationOrder = new List<string>(),
                GenerationNotes = new Dictionary<string, string>()
            };
            
            // Pokus o extrakci hlavních komponent
            var componentMatches = Regex.Matches(
                planningResponse,
                @"(?:Component|Komponenta|Module|Modul)[\s:]+(\w+)[:\s]*([^\n]*)",
                RegexOptions.IgnoreCase);
            
            foreach (Match match in componentMatches)
            {
                if (match.Groups.Count >= 3)
                {
                    string name = match.Groups[1].Value.Trim();
                    string description = match.Groups[2].Value.Trim();
                    
                    plan.MainComponents.Add(name);
                    plan.ComponentDetails[name] = description;
                }
            }
            
            // Pokus o extrakci závislostí
            var dependencyMatches = Regex.Matches(
                planningResponse,
                @"(?:Dependency|Závislost|Depends on|Závisí na)[:\s]+(\w+)[:\s]*(?:depends on|závisí na|requires|potřebuje)[:\s]*([\w,\s]+)",
                RegexOptions.IgnoreCase);
            
            foreach (Match match in dependencyMatches)
            {
                if (match.Groups.Count >= 3)
                {
                    string component = match.Groups[1].Value.Trim();
                    string dependencies = match.Groups[2].Value.Trim();
                    
                    var dependencyList = dependencies
                        .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .ToList();
                    
                    plan.DependencyGraph[component] = dependencyList;
                }
            }
            
            // Pokus o extrakci pořadí implementace
            var orderMatch = Regex.Match(
                planningResponse,
                @"(?:Implementation Order|Pořadí implementace)[:\s]*([\w,\s]+)",
                RegexOptions.IgnoreCase);
            
            if (orderMatch.Groups.Count >= 2)
            {
                string orderText = orderMatch.Groups[1].Value.Trim();
                
                plan.ImplementationOrder = orderText
                    .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim())
                    .Where(o => !string.IsNullOrEmpty(o))
                    .ToList();
            }
            
            // Pokud extrakce pořadí selhala, použijeme topologické řazení závislostí
            if (plan.ImplementationOrder.Count == 0 && plan.DependencyGraph.Count > 0)
            {
                plan.ImplementationOrder = TopologicalSort(plan.DependencyGraph, plan.MainComponents);
            }
            
            // Pokud je pořadí stále prázdné, použijeme pořadí hlavních komponent
            if (plan.ImplementationOrder.Count == 0)
            {
                plan.ImplementationOrder = plan.MainComponents.ToList();
            }
            
            // Extrakce obecných poznámek pro generování
            var notesMatches = Regex.Matches(
                planningResponse,
                @"(?:Note|Poznámka)[:\s]+(\w+)[:\s]*([^\n]*)",
                RegexOptions.IgnoreCase);
            
            foreach (Match match in notesMatches)
            {
                if (match.Groups.Count >= 3)
                {
                    string component = match.Groups[1].Value.Trim();
                    string note = match.Groups[2].Value.Trim();
                    
                    plan.GenerationNotes[component] = note;
                }
            }
            
            return plan;
        }
        
        /// <summary>
        /// Provádí topologické řazení komponent podle jejich závislostí
        /// </summary>
        private List<string> TopologicalSort(
            Dictionary<string, List<string>> dependencyGraph,
            List<string> allComponents)
        {
            // Vytvoření reverzního grafu závislostí
            var reversedGraph = new Dictionary<string, List<string>>();
            
            // Inicializace všech komponent
            foreach (var component in allComponents)
            {
                reversedGraph[component] = new List<string>();
            }
            
            // Naplnění reverzního grafu
            foreach (var entry in dependencyGraph)
            {
                string component = entry.Key;
                List<string> dependencies = entry.Value;
                
                foreach (string dependency in dependencies)
                {
                    if (!reversedGraph.ContainsKey(dependency))
                    {
                        reversedGraph[dependency] = new List<string>();
                    }
                    
                    reversedGraph[dependency].Add(component);
                }
            }
            
            // Nalezení počátečních uzlů (bez závislostí)
            var startNodes = allComponents
                .Where(c => !dependencyGraph.ContainsKey(c) || dependencyGraph[c].Count == 0)
                .ToList();
            
            var result = new List<string>();
            var visited = new HashSet<string>();
            
            // Rekurzivní průchod grafem
            foreach (var node in startNodes)
            {
                VisitNode(node, reversedGraph, visited, result);
            }
            
            // Pokud jsme nenavštívili všechny uzly, přidáme zbývající (může obsahovat cykly)
            foreach (var component in allComponents)
            {
                if (!result.Contains(component))
                {
                    result.Add(component);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Pomocná metoda pro rekurzivní průchod grafem při topologickém řazení
        /// </summary>
        private void VisitNode(
            string node,
            Dictionary<string, List<string>> graph,
            HashSet<string> visited,
            List<string> result)
        {
            if (visited.Contains(node))
            {
                return;
            }
            
            visited.Add(node);
            
            if (graph.ContainsKey(node))
            {
                foreach (var dependent in graph[node])
                {
                    VisitNode(dependent, graph, visited, result);
                }
            }
            
            result.Add(node);
        }
        
        /// <summary>
        /// Druhý pass - Detailní analýza a rozdělení na komponenty s použitím plánu
        /// </summary>
        private async Task<List<ApplicationComponent>> AnalyzeAndDivideIntoComponentsAsync(
            string applicationDescription,
            GenerationPlan plan,
            string conversationId)
        {
            LogMessage("Pass 2: Detailní analýza a rozdělení na komponenty");
            
            var components = new List<ApplicationComponent>();
            
            // Pokud máme k dispozici plán, vytvoříme komponenty podle něj
            if (plan.MainComponents.Count > 0)
            {
                foreach (var componentName in plan.MainComponents)
                {
                    string description = plan.ComponentDetails.ContainsKey(componentName)
                        ? plan.ComponentDetails[componentName]
                        : $"Component for {componentName}";
                    
                    ComponentType type = DetermineComponentType(componentName, description);
                    
                    // Zjištění závislostí
                    List<string> dependencies = plan.DependencyGraph.ContainsKey(componentName)
                        ? plan.DependencyGraph[componentName]
                        : new List<string>();
                    
                    // Zjištění poznámek
                    string notes = plan.GenerationNotes.ContainsKey(componentName)
                        ? plan.GenerationNotes[componentName]
                        : string.Empty;
                    
                    components.Add(new ApplicationComponent
                    {
                        Name = componentName,
                        Description = description,
                        Type = type,
                        Dependencies = dependencies,
                        GenerationNotes = notes
                    });
                }
                
                LogMessage($"Vytvořeno {components.Count} komponent z plánu");
                return components;
            }
            
            // Vytvoření kontextu pro prompt
            var workflowContext = new WorkflowContext
            {
                Description = applicationDescription,
                OperationId = conversationId,
                CurrentStage = WorkflowStage.Planning
            };
            
            // Tvorba promptu pro detailní analýzu a rozdělení
            string prompt = await _promptTranslator.TranslateToModelPromptAsync(
                $"Analyze this application and divide it into detailed logical components with dependencies:\n\n{applicationDescription}\n\nFor each component specify:\n1. Name\n2. Purpose\n3. Key functionality\n4. Dependencies on other components\n5. Implementation priority",
                "codellama:13b",
                PromptType.Planning,
                workflowContext);
            
            // Získání odpovědi od LLM
            string response = await _llmClient.GenerateAsync(prompt);
            
            // Zaznamenání odpovědi do kontextu
            await _contextManager.AddMessageAsync(
                conversationId,
                new ConversationMessage
                {
                    Role = "assistant",
                    Content = response,
                    Timestamp = DateTime.Now,
                    MessageType = MessageType.Response
                });
            
            // Parsování komponent z odpovědi
            components = ParseDetailedComponentsFromResponse(response, applicationDescription);
            
            // Ověření minimálního počtu komponent
            if (components.Count < _config.MinComponentSplitThreshold)
            {
                LogMessage($"Detekováno pouze {components.Count} komponent, což je pod prahem {_config.MinComponentSplitThreshold}. Rozšiřuji komponenty.");
                components = ExpandComponentsIfNeeded(components, applicationDescription);
            }
            
            // Publikujeme událost o rozdělení na komponenty
            _eventBus.Publish(new ComponentsIdentifiedEvent(conversationId, components));
            
            return components;
        }
        
        /// <summary>
        /// Parsuje detailní komponenty z odpovědi LLM
        /// </summary>
        private List<ApplicationComponent> ParseDetailedComponentsFromResponse(
            string response,
            string applicationDescription)
        {
            var components = new List<ApplicationComponent>();
            
            // Regex pro zachycení detailů komponenty:
            // Komponenta: [název]
            // Účel: [účel]
            // ...
            var componentSections = Regex.Matches(
                response,
                @"(?:Component|Komponenta)[:\s]+(\w+)[\s\S]*?(?=(?:Component|Komponenta)[:\s]+\w+|$)",
                RegexOptions.IgnoreCase);
            
            foreach (Match section in componentSections)
            {
                string sectionText = section.Value;
                string componentName = section.Groups[1].Value.Trim();
                
                // Extrakce účelu/popisu
                string description = ExtractDetail(sectionText, "Purpose|Účel|Description|Popis");
                
                // Extrakce funkcionality
                string functionality = ExtractDetail(sectionText, "Functionality|Funkcionalita|Features|Funkce");
                
                // Extrakce závislostí
                string dependenciesText = ExtractDetail(sectionText, "Dependencies|Závislosti");
                List<string> dependencies = ParseDependenciesList(dependenciesText);
                
                // Extrakce priority
                string priorityText = ExtractDetail(sectionText, "Priority|Priorita");
                int priority = ParsePriority(priorityText);
                
                ComponentType type = DetermineComponentType(componentName, description + " " + functionality);
                
                components.Add(new ApplicationComponent
                {
                    Name = componentName,
                    Description = description,
                    Type = type,
                    Functionality = functionality,
                    Dependencies = dependencies,
                    Priority = priority
                });
            }
            
            // Pokud regex selhal, pokusíme se o jednodušší extrakci
            if (components.Count == 0)
            {
                components = ParseComponentsFromResponse(response, applicationDescription);
            }
            
            return components;
        }
        
        /// <summary>
        /// Extrahuje detail komponenty podle regex patternu
        /// </summary>
        private string ExtractDetail(string text, string detailPattern)
        {
            var match = Regex.Match(
                text,
                $@"(?:{detailPattern})[:\s]+([\s\S]*?)(?=(?:\n\s*\w+[:\s]+|\Z))",
                RegexOptions.IgnoreCase);
            
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Trim();
            }
            
            return string.Empty;
        }
        
        /// <summary>
        /// Parsuje seznam závislostí
        /// </summary>
        private List<string> ParseDependenciesList(string dependenciesText)
        {
            if (string.IsNullOrEmpty(dependenciesText))
            {
                return new List<string>();
            }
            
            return dependenciesText
                .Split(new[] { ',', '\n', '-', '•' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrEmpty(d) && d.Length > 1) // Ignorujeme jednoznakové závislosti
                .ToList();
        }
        
        /// <summary>
        /// Parsuje prioritu implementace
        /// </summary>
        private int ParsePriority(string priorityText)
        {
            if (string.IsNullOrEmpty(priorityText))
            {
                return 999; // Výchozí nízká priorita
            }
            
            // Pokus o extrakci čísla
            var match = Regex.Match(priorityText, @"(\d+)");
            if (match.Success && match.Groups.Count > 1)
            {
                if (int.TryParse(match.Groups[1].Value, out int priority))
                {
                    return priority;
                }
            }
            
            // Pokus o určení priority podle textu
            priorityText = priorityText.ToLower();
            if (priorityText.Contains("high") || priorityText.Contains("vysoká"))
            {
                return 1;
            }
            else if (priorityText.Contains("medium") || priorityText.Contains("střední"))
            {
                return 2;
            }
            else if (priorityText.Contains("low") || priorityText.Contains("nízká"))
            {
                return 3;
            }
            
            return 999; // Výchozí nízká priorita
        }
        
        /// <summary>
        /// Rozšíří komponenty, pokud je jich příliš málo
        /// </summary>
        private List<ApplicationComponent> ExpandComponentsIfNeeded(
            List<ApplicationComponent> components,
            string applicationDescription)
        {
            // Pokud máme dostatek komponent, nemusíme nic rozšiřovat
            if (components.Count >= _config.MinComponentSplitThreshold)
            {
                return components;
            }
            
            // Najdeme komponenty UI, které můžeme rozdělit
            var uiComponents = components.Where(c => c.Type == ComponentType.UI).ToList();
            
            foreach (var uiComponent in uiComponents)
            {
                // Rozdělíme UI komponentu na menší části
                if (uiComponent.Name.Contains("Form") || uiComponent.Name.Contains("UI"))
                {
                    // Přidáme komponentu pro hlavní menu
                    components.Add(new ApplicationComponent
                    {
                        Name = $"{uiComponent.Name}Menu",
                        Description = $"Menu for {uiComponent.Name}",
                        Type = ComponentType.UI,
                        Dependencies = new List<string> { uiComponent.Name }
                    });
                    
                    // Přidáme komponentu pro hlavní panel
                    components.Add(new ApplicationComponent
                    {
                        Name = $"{uiComponent.Name}Panel",
                        Description = $"Main panel for {uiComponent.Name}",
                        Type = ComponentType.UI,
                        Dependencies = new List<string> { uiComponent.Name }
                    });
                }
            }
            
            // Najdeme komponenty pro logiku, které můžeme rozdělit
            var logicComponents = components.Where(c => c.Type == ComponentType.Logic).ToList();
            
            foreach (var logicComponent in logicComponents)
            {
                // Rozdělíme logickou komponentu na menší části
                components.Add(new ApplicationComponent
                {
                    Name = $"{logicComponent.Name}Helper",
                    Description = $"Helper for {logicComponent.Name}",
                    Type = ComponentType.Logic,
                    Dependencies = new List<string> { logicComponent.Name }
                });
            }
            
            // Přidáme obecné komponenty, pokud stále nemáme dostatek
            if (components.Count < _config.MinComponentSplitThreshold)
            {
                components.Add(new ApplicationComponent
                {
                    Name = "Constants",
                    Description = "Constants and configuration values",
                    Type = ComponentType.Data
                });
                
                components.Add(new ApplicationComponent
                {
                    Name = "Utilities",
                    Description = "Utility functions and extensions",
                    Type = ComponentType.Logic
                });
                
                components.Add(new ApplicationComponent
                {
                    Name = "Models",
                    Description = "Data models and entities",
                    Type = ComponentType.Data
                });
            }
            
            return components;
        }
        
        /// <summary>
        /// Parsuje komponenty z odpovědi LLM
        /// </summary>
        private List<ApplicationComponent> ParseComponentsFromResponse(
            string response, 
            string applicationDescription)
        {
            var components = new List<ApplicationComponent>();
            
            // Pokus o nalezení strukturovaného seznamu komponent
            var componentMatches = Regex.Matches(
                response,
                @"(?:Component|Komponenta|Module|Modul|Class|Třída)[:\s]+(\w+)[:\s]*([^\n]*)\n",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            if (componentMatches.Count > 0)
            {
                foreach (Match match in componentMatches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string name = match.Groups[1].Value.Trim();
                        string description = match.Groups[2].Value.Trim();
                        
                        components.Add(new ApplicationComponent
                        {
                            Name = name,
                            Description = description,
                            Type = DetermineComponentType(name, description)
                        });
                    }
                }
            }
            
            // Pokud nebyl nalezen strukturovaný popis, zkusíme heuristicky vytvořit komponenty
            if (components.Count == 0)
            {
                LogMessage("Nepodařilo se rozpoznat komponenty, vytvářím základní sadu", MessageSeverity.Warning);
                
                // Pro WinForms aplikace vytvoříme vždy alespoň hlavní formulář a business logiku
                components.Add(new ApplicationComponent
                {
                    Name = "MainForm",
                    Description = $"Main form for the application: {applicationDescription}",
                    Type = ComponentType.UI
                });
                
                // Přidáme helper třídy
                components.Add(new ApplicationComponent
                {
                    Name = "BusinessLogic",
                    Description = $"Business logic for the application: {applicationDescription}",
                    Type = ComponentType.Logic
                });
                
                // Pokud je v popisu zmínka o datech, přidáme i datovou vrstvu
                if (applicationDescription.Contains("data") || 
                    applicationDescription.Contains("databáze") || 
                    applicationDescription.Contains("database") ||
                    applicationDescription.Contains("soubor") ||
                    applicationDescription.Contains("file"))
                {
                    components.Add(new ApplicationComponent
                    {
                        Name = "DataAccess",
                        Description = $"Data access logic for the application: {applicationDescription}",
                        Type = ComponentType.Data
                    });
                }
            }
            
            return components;
        }
        
        /// <summary>
        /// Určí typ komponenty podle názvu a popisu
        /// </summary>
        private ComponentType DetermineComponentType(string name, string description)
        {
            // Určení typu podle názvu
            string nameLower = name.ToLower();
            if (nameLower.Contains("form") || 
                nameLower.Contains("panel") || 
                nameLower.Contains("control") || 
                nameLower.Contains("view") ||
                nameLower.Contains("ui") ||
                nameLower.EndsWith("tab"))
            {
                return ComponentType.UI;
            }
            
            if (nameLower.Contains("data") || 
                nameLower.Contains("repository") || 
                nameLower.Contains("db") || 
                nameLower.Contains("storage") ||
                nameLower.Contains("model"))
            {
                return ComponentType.Data;
            }
            
            // Určení typu podle popisu
            string descLower = description.ToLower();
            if (descLower.Contains("ui") || 
                descLower.Contains("formulář") || 
                descLower.Contains("form") || 
                descLower.Contains("panel") ||
                descLower.Contains("zobrazení") ||
                descLower.Contains("view"))
            {
                return ComponentType.UI;
            }
            
            if (descLower.Contains("data") || 
                descLower.Contains("databáze") || 
                descLower.Contains("database") || 
                descLower.Contains("model") ||
                descLower.Contains("ukládání") ||
                descLower.Contains("storage"))
            {
                return ComponentType.Data;
            }
            
            // Výchozí typ je logika
            return ComponentType.Logic;
        }
        
        /// <summary>
        /// Druhý pass - Generování implementačního plánu pro každou komponentu
        /// </summary>
        private async Task GenerateImplementationPlansAsync(
            List<ApplicationComponent> components, 
            string conversationId)
        {
            LogMessage("Pass 2: Generování implementačních plánů pro komponenty");
            
            foreach (var component in components)
            {
                LogMessage($"Generuji implementační plán pro komponentu {component.Name}");
                
                // Vytvoření kontextu pro prompt
                var workflowContext = new WorkflowContext
                {
                    Description = component.Description,
                    OperationId = conversationId,
                    CurrentStage = WorkflowStage.Planning
                };
                
                // Tvorba promptu pro plánování implementace
                string prompt = await _promptTranslator.TranslateToModelPromptAsync(
                    $"Create a detailed implementation plan for this component: {component.Name} - {component.Description}",
                    "codellama:7b", // Použijeme menší model pro plánování
                    PromptType.Planning,
                    workflowContext);
                
                // Získání odpovědi od LLM
                string response = await _llmClient.GenerateAsync(prompt);
                
                // Zaznamenání odpovědi do kontextu
                await _contextManager.AddMessageAsync(
                    conversationId,
                    new ConversationMessage
                    {
                        Role = "assistant",
                        Content = response,
                        Timestamp = DateTime.Now,
                        MessageType = MessageType.Response
                    });
                
                // Uložení implementačního plánu do komponenty
                component.ImplementationPlan = response;
                
                LogMessage($"Implementační plán pro komponentu {component.Name} vygenerován");
            }
        }
        
        /// <summary>
        /// Třetí pass - Generování kódu pro jednotlivé komponenty
        /// </summary>
        private async Task GenerateComponentCodeAsync(
            List<ApplicationComponent> components, 
            string conversationId)
        {
            LogMessage("Pass 3: Generování kódu pro jednotlivé komponenty");
            
            foreach (var component in components)
            {
                LogMessage($"Generuji kód pro komponentu {component.Name}");
                
                // Vytvoření kontextu pro prompt
                var workflowContext = new WorkflowContext
                {
                    Description = component.Description,
                    DevelopmentPlan = component.ImplementationPlan,
                    OperationId = conversationId,
                    CurrentStage = WorkflowStage.Generation
                };
                
                // Určení optimálního modelu podle typu komponenty
                string modelId = SelectModelForComponentType(component.Type);
                
                // Tvorba promptu pro generování kódu
                string prompt = await _promptTranslator.TranslateToModelPromptAsync(
                    $"Generate C# code for this component: {component.Name} - {component.Description} according to this implementation plan: {component.ImplementationPlan}",
                    modelId,
                    PromptType.CodeGeneration,
                    workflowContext);
                
                // Získání odpovědi od LLM
                string response = await _llmClient.GenerateAsync(prompt);
                
                // Zaznamenání odpovědi do kontextu
                await _contextManager.AddMessageAsync(
                    conversationId,
                    new ConversationMessage
                    {
                        Role = "assistant",
                        Content = response,
                        Timestamp = DateTime.Now,
                        MessageType = MessageType.Response
                    });
                
                // Extrakce kódu z odpovědi
                string code = ExtractCode(response);
                
                // Uložení kódu do komponenty
                component.Code = code;
                
                LogMessage($"Kód pro komponentu {component.Name} vygenerován ({code.Length} znaků)");
            }
        }
        
        /// <summary>
        /// Vybírá optimální model podle typu komponenty
        /// </summary>
        private string SelectModelForComponentType(ComponentType componentType)
        {
            switch (componentType)
            {
                case ComponentType.UI:
                    return "codellama:13b"; // Větší model pro UI
                case ComponentType.Logic:
                    return "codellama:7b"; // Střední model pro logiku
                case ComponentType.Data:
                    return "codellama:7b"; // Střední model pro data
                default:
                    return "codellama:13b"; // Výchozí je větší model
            }
        }
        
        /// <summary>
        /// Šestý pass - Integrace komponent s řešením konfliktů
        /// </summary>
        private async Task<string> IntegrateComponentsAsync(
            List<ApplicationComponent> components, 
            string applicationDescription,
            ApplicationType applicationType,
            GenerationPlan plan,
            string conversationId)
        {
            LogMessage("Pass 6: Integrace komponent s řešením konfliktů");
            
            // Vytvoření kontextu pro prompt
            var workflowContext = new WorkflowContext
            {
                Description = applicationDescription,
                OperationId = conversationId,
                CurrentStage = WorkflowStage.Integration
            };
            
            // Vyfiltrujeme jen komponenty s vygenerovaným kódem
            var componentsWithCode = components
                .Where(c => !string.IsNullOrEmpty(c.Code))
                .ToList();
            
            if (componentsWithCode.Count == 0)
            {
                LogMessage("Žádné komponenty s kódem k integraci", MessageSeverity.Error);
                return CreateFallbackCode(applicationDescription);
            }
            
            // Pokud máme jen jednu komponentu, nepotřebujeme integraci
            if (componentsWithCode.Count == 1)
            {
                LogMessage("Pouze jedna komponenta s kódem, integrace není potřeba");
                return componentsWithCode[0].Code;
            }
            
            // Vytvoření přehledu komponent pro LLM
            var componentsSummary = new StringBuilder();
            componentsSummary.AppendLine("Here are the components to integrate:");
            
            // Seřadíme komponenty podle implementačního pořadí
            var orderedComponents = componentsWithCode
                .OrderBy(c => {
                    int index = plan.ImplementationOrder.IndexOf(c.Name);
                    return index >= 0 ? index : int.MaxValue;
                })
                .ToList();
            
            foreach (var component in orderedComponents)
            {
                componentsSummary.AppendLine($"# Component: {component.Name} ({component.Type})");
                componentsSummary.AppendLine($"Description: {component.Description}");
                
                // Přidáme informace o závislostech
                if (component.Dependencies != null && component.Dependencies.Count > 0)
                {
                    componentsSummary.AppendLine($"Dependencies: {string.Join(", ", component.Dependencies)}");
                }
                
                componentsSummary.AppendLine("Code:");
                componentsSummary.AppendLine("```csharp");
                componentsSummary.AppendLine(component.Code);
                componentsSummary.AppendLine("```");
                componentsSummary.AppendLine();
            }
            
            // Zjistíme, zda můžeme použít LLM pro integraci (závisí na velikosti kódu)
            bool canUseLlmIntegration = _config.AutoIntegrate && 
                                       (components.Sum(c => c.Code?.Length ?? 0) < 12000); // Zvýšený limit pro moderní modely
            
            if (canUseLlmIntegration)
            {
                LogMessage("Integrace komponent pomocí LLM");
                
                // Tvorba promptu pro integraci
                string prompt = await _promptTranslator.TranslateToModelPromptAsync(
                    $"Integrate these components into a complete C# application:\n\nApplication Description: {applicationDescription}\nApplication Type: {applicationType}\n\n{componentsSummary.ToString()}\n\nRequirements:\n1. Combine all components properly preserving their functionality\n2. Resolve any namespace conflicts\n3. Ensure proper dependencies between components\n4. Add a Main method with [STAThread] attribute for WinForms apps\n5. Fix any name collisions or duplicate definitions\n6. Make sure the final code is complete and compilable",
                    "codellama:13b", // Větší model pro komplexní integraci
                    PromptType.CodeGeneration,
                    workflowContext);
                
                // Získání odpovědi od LLM
                string response = await _llmClient.GenerateAsync(prompt);
                
                // Zaznamenání odpovědi do kontextu
                await _contextManager.AddMessageAsync(
                    conversationId,
                    new ConversationMessage
                    {
                        Role = "assistant",
                        Content = response,
                        Timestamp = DateTime.Now,
                        MessageType = MessageType.Response
                    });
                
                // Extrakce kódu z odpovědi
                string integratedCode = ExtractCode(response);
                
                // Kontrola kvality integrovaného kódu
                if (!string.IsNullOrWhiteSpace(integratedCode) && 
                    integratedCode.Length > componentsWithCode.Max(c => c.Code?.Length ?? 0) * 0.8)
                {
                    // Ověříme, že integrovaný kód obsahuje všechny komponenty
                    bool containsAllComponents = orderedComponents.All(c => 
                        integratedCode.Contains(c.Name) || 
                        integratedCode.Contains(GetClassNameFromCode(c.Code)));
                    
                    // Ověříme základní requirmenty podle typu aplikace
                    bool hasAppRequirements = applicationType != ApplicationType.WinForms || 
                                            (integratedCode.Contains("[STAThread]") && 
                                             integratedCode.Contains("Application.Run"));
                    
                    if (containsAllComponents && hasAppRequirements)
                    {
                        LogMessage("Integrace komponent pomocí LLM úspěšná");
                        
                        // Publikujeme událost o úspěšné integraci
                        _eventBus.Publish(new ComponentsIntegratedEvent(
                            conversationId, 
                            true, 
                            "LLM Integration", 
                            componentsWithCode.Count));
                        
                        return integratedCode;
                    }
                }
                
                LogMessage("Integrace komponent pomocí LLM neúspěšná, přepínám na manuální integraci", MessageSeverity.Warning);
            }
            
            // Pro větší projekty nebo když LLM integrace selhala, provedeme manuální integraci
            LogMessage("Manuální integrace komponent");
            
            string manuallyIntegratedCode = ManuallyIntegrateComponents(
                orderedComponents, 
                applicationDescription, 
                applicationType);
            
            // Publikujeme událost o manuální integraci
            _eventBus.Publish(new ComponentsIntegratedEvent(
                conversationId, 
                true, 
                "Manual Integration", 
                componentsWithCode.Count));
            
            return manuallyIntegratedCode;
        }
        
        /// <summary>
        /// Manuálně integruje komponenty do jednoho celku
        /// </summary>
        private string ManuallyIntegrateComponents(
            List<ApplicationComponent> components, 
            string applicationDescription,
            ApplicationType applicationType)
        {
            var codeBuilder = new StringBuilder();
            
            // 1. Přidání using direktiv
            var usingStatements = ExtractUsingStatements(components);
            foreach (var usingStmt in usingStatements)
            {
                codeBuilder.AppendLine(usingStmt);
            }
            codeBuilder.AppendLine();
            
            // 2. Vytvoření namespace
            codeBuilder.AppendLine("namespace GeneratedApp");
            codeBuilder.AppendLine("{");
            
            // 3. Přidání kódu komponent ve správném pořadí
            // Nejprve přidáme datové třídy, pak logiku a nakonec UI
            var orderedComponents = components
                .OrderBy(c => {
                    // Pořadí podle typu - nejprve data, pak logika, nakonec UI
                    switch (c.Type)
                    {
                        case ComponentType.Data: return 0;
                        case ComponentType.Logic: return 1;
                        case ComponentType.UI: return 2;
                        default: return 3;
                    }
                })
                .ToList();
            
            // Příprava mapy názvů tříd z komponent pro detekci konfliktů
            var classNames = new Dictionary<string, string>();
            
            foreach (var component in orderedComponents)
            {
                // Extrahujeme název třídy
                string className = GetClassNameFromCode(component.Code);
                
                // Kontrola konfliktu názvů
                if (classNames.ContainsKey(className))
                {
                    // Přejmenování třídy v případě konfliktu
                    string newClassName = $"{className}_{component.Name}";
                    string modifiedCode = RenameClass(component.Code, className, newClassName);
                    
                    // Zaznamenáme nové jméno třídy
                    classNames[newClassName] = component.Name;
                    
                    string classCode = ExtractClassDefinition(modifiedCode);
                    codeBuilder.AppendLine(classCode);
                }
                else
                {
                    // Zaznamenáme jméno třídy pro detekci konfliktů
                    classNames[className] = component.Name;
                    
                    string classCode = ExtractClassDefinition(component.Code);
                    codeBuilder.AppendLine(classCode);
                }
                
                codeBuilder.AppendLine();
            }
            
            // 4. Pro Windows Forms aplikace přidáme Program class s Main metodou, pokud neexistuje
            if (applicationType == ApplicationType.WinForms)
            {
                bool hasMainMethod = components.Any(c => c.Code.Contains("static void Main("));
                
                if (!hasMainMethod)
                {
                    // Najdeme třídu formuláře - nejprve hledáme podle dědičnosti od Form
                    var formClass = components
                        .Where(c => c.Type == ComponentType.UI)
                        .FirstOrDefault(c => c.Code.Contains(" : Form") || c.Code.Contains(": Form"));
                    
                    // Pokud nenajdeme přímého potomka Form, hledáme jakoukoliv UI komponentu
                    if (formClass == null)
                    {
                        formClass = components.FirstOrDefault(c => c.Type == ComponentType.UI);
                    }
                    
                    // Pokud stále nemáme třídu formuláře, vezmeme první komponentu
                    string formClassName = formClass != null 
                        ? GetClassNameFromCode(formClass.Code) 
                        : "MainForm";
                    
                    // Pokud název třídy byl přejmenován kvůli konfliktům, použijeme nový název
                    if (classNames.ContainsValue(formClass?.Name) && !classNames.ContainsKey(formClassName))
                    {
                        var entry = classNames.FirstOrDefault(c => c.Value == formClass.Name);
                        formClassName = entry.Key;
                    }
                    
                    codeBuilder.AppendLine("    public static class Program");
                    codeBuilder.AppendLine("    {");
                    codeBuilder.AppendLine("        [STAThread]");
                    codeBuilder.AppendLine("        static void Main()");
                    codeBuilder.AppendLine("        {");
                    codeBuilder.AppendLine("            Application.EnableVisualStyles();");
                    codeBuilder.AppendLine("            Application.SetCompatibleTextRenderingDefault(false);");
                    codeBuilder.AppendLine($"            Application.Run(new {formClassName}());");
                    codeBuilder.AppendLine("        }");
                    codeBuilder.AppendLine("    }");
                    codeBuilder.AppendLine();
                }
            }
            
            // 5. Uzavření namespace
            codeBuilder.AppendLine("}");
            
            return codeBuilder.ToString();
        }
        
        /// <summary>
        /// Přejmenuje třídu v kódu
        /// </summary>
        private string RenameClass(string code, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(code))
            {
                return code;
            }
            
            // Nahrazení deklarace třídy
            string patternClass = $@"(public|internal|private|protected)(\s+static)?\s+class\s+{Regex.Escape(oldName)}(\s|:|\n)";
            string replacement = $"$1$2 class {newName}$3";
            
            string result = Regex.Replace(code, patternClass, replacement);
            
            // Nahrazení konstruktorů
            string patternConstructor = $@"(public|internal|private|protected)(\s+static)?\s+{Regex.Escape(oldName)}\s*\(";
            string replacementConstructor = $"$1$2 {newName}(";
            
            result = Regex.Replace(result, patternConstructor, replacementConstructor);
            
            // Nahrazení statických referencí na třídu
            string patternStaticRef = $@"({Regex.Escape(oldName)})\.";
            string replacementStaticRef = $"{newName}.";
            
            result = Regex.Replace(result, patternStaticRef, replacementStaticRef);
            
            // Nahrazení instancí new ClassName()
            string patternInstance = $@"new\s+{Regex.Escape(oldName)}\s*\(";
            string replacementInstance = $"new {newName}(";
            
            result = Regex.Replace(result, patternInstance, replacementInstance);
            
            return result;
        }
        
        /// <summary>
        /// Extrahuje using direktivy z komponent
        /// </summary>
        private List<string> ExtractUsingStatements(List<ApplicationComponent> components)
        {
            var usingStatements = new HashSet<string>();
            
            // Základní using direktivy pro Windows Forms
            usingStatements.Add("using System;");
            usingStatements.Add("using System.Collections.Generic;");
            usingStatements.Add("using System.Linq;");
            
            // Přidáme using direktivy z komponent
            foreach (var component in components)
            {
                var matches = Regex.Matches(
                    component.Code, 
                    @"^using\s+[^;]+;",
                    RegexOptions.Multiline);
                
                foreach (Match match in matches)
                {
                    usingStatements.Add(match.Value);
                }
                
                // Pro UI komponenty přidáme Windows Forms
                if (component.Type == ComponentType.UI)
                {
                    usingStatements.Add("using System.Windows.Forms;");
                    usingStatements.Add("using System.Drawing;");
                }
            }
            
            return usingStatements.ToList();
        }
        
        /// <summary>
        /// Extrahuje definici třídy z kódu
        /// </summary>
        private string ExtractClassDefinition(string code)
        {
            // Odstraní using direktivy
            var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var nonUsingLines = lines.Where(l => !l.TrimStart().StartsWith("using ")).ToList();
            
            // Odstraní namespace, pokud existuje
            StringBuilder result = new StringBuilder();
            bool inNamespace = false;
            int indentLevel = 1; // Počáteční úroveň odsazení
            
            foreach (var line in nonUsingLines)
            {
                if (line.Contains("namespace "))
                {
                    inNamespace = true;
                    continue;
                }
                
                // Sledujeme úroveň odsazení
                if (inNamespace)
                {
                    if (line.Contains("{"))
                    {
                        indentLevel++;
                    }
                    
                    if (line.Contains("}"))
                    {
                        indentLevel--;
                        if (indentLevel == 0)
                        {
                            inNamespace = false;
                            continue;
                        }
                    }
                    
                    // Přidáme řádek s patřičným odsazením (jednou úrovní)
                    result.AppendLine("    " + line.TrimStart());
                }
                else
                {
                    // Přidáme řádek s jednou úrovní odsazení
                    result.AppendLine("    " + line);
                }
            }
            
            return result.ToString();
        }
        
        /// <summary>
        /// Získá název třídy z kódu
        /// </summary>
        private string GetClassNameFromCode(string code)
        {
            var match = Regex.Match(code, @"class\s+(\w+)");
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
            
            return "MainClass";
        }
        
        /// <summary>
        /// Fallback jednoprůchodové generování kódu
        /// </summary>
        private async Task<string> FallbackSinglePassGenerationAsync(
            string applicationDescription, 
            string developmentPlan, 
            ApplicationType applicationType)
        {
            LogMessage("Fallback jednoprůchodové generování kódu");
            
            // Vytvoření kontextu pro prompt
            var workflowContext = new WorkflowContext
            {
                Description = applicationDescription,
                DevelopmentPlan = developmentPlan,
                OperationId = Guid.NewGuid().ToString(),
                CurrentStage = WorkflowStage.Generation
            };
            
            // Tvorba promptu pro generování kódu
            string prompt = await _promptTranslator.TranslateToModelPromptAsync(
                applicationDescription,
                "codellama:13b",
                PromptType.CodeGeneration,
                workflowContext);
            
            // Získání odpovědi od LLM
            string response = await _llmClient.GenerateAsync(prompt);
            
            // Extrakce kódu z odpovědi
            string code = ExtractCode(response);
            
            // Validace kódu
            if (string.IsNullOrWhiteSpace(code) || code.Length < 100)
            {
                LogMessage("Vygenerovaný kód je příliš krátký nebo prázdný, používám fallback kód", MessageSeverity.Error);
                return CreateFallbackCode(applicationDescription);
            }
            
            return code;
        }

        /// <summary>
        /// Opravuje kód s chybami
        /// </summary>
        public async Task<string> RepairCodeAsync(
            string codeWithErrors,
            string errors,
            string repairStrategy)
        {
            LogMessage($"Opravuji kód s chybami: {errors}");
            _totalRepairAttempts++;

            try
            {
                // Předzpracování chybových zpráv pro lepší kontext
                string processedErrors = ProcessErrorMessages(errors);
                
                // Vytvoření kontextu pro prompt
                var workflowContext = new WorkflowContext
                {
                    CurrentCode = codeWithErrors,
                    Errors = processedErrors,
                    OperationId = Guid.NewGuid().ToString(),
                    CurrentStage = WorkflowStage.ErrorRepair
                };

                // Tvorba promptu pro opravu kódu
                string prompt = await _promptTranslator.TranslateToModelPromptAsync(
                    $"Fix these errors in the code: {processedErrors}",
                    "codellama:7b",
                    PromptType.ErrorRepair,
                    workflowContext);
                
                // Získání odpovědi od LLM
                string response = await _llmClient.GenerateAsync(prompt);

                // Extrakce kódu z odpovědi
                string repairedCode = ExtractCode(response);

                // Ověření, že opravený kód není prázdný nebo příliš krátký
                if (string.IsNullOrWhiteSpace(repairedCode) || repairedCode.Length < 50)
                {
                    LogMessage("Opravený kód je příliš krátký nebo prázdný, vracím původní kód", MessageSeverity.Warning);
                    return codeWithErrors;
                }

                // Ověření, že kód obsahuje základní části
                if (!repairedCode.Contains("public class") || !repairedCode.Contains("Form"))
                {
                    LogMessage("Opravený kód neobsahuje základní části WinForms aplikace, vracím původní kód", MessageSeverity.Warning);
                    return codeWithErrors;
                }

                _successfulRepairs++;
                LogMessage("Kód byl úspěšně opraven");
                return repairedCode;
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při opravě kódu: {ex.Message}", MessageSeverity.Error);
                return codeWithErrors; // V případě chyby vracíme původní kód
            }
        }

        /// <summary>
        /// Testuje aplikaci pomocí LLM na základě popisu formuláře
        /// </summary>
        public async Task<string> TestApplicationAsync(
            string formDescription,
            string applicationDescription)
        {
            LogMessage("Testuji aplikaci pomocí LLM");

            try
            {
                // Vytvoření kontextu pro prompt
                var workflowContext = new WorkflowContext
                {
                    Description = applicationDescription,
                    FormDescription = formDescription,
                    OperationId = Guid.NewGuid().ToString(),
                    CurrentStage = WorkflowStage.Testing
                };
                
                // Tvorba promptu pro testování
                string prompt = await _promptTranslator.TranslateToModelPromptAsync(
                    $"Test if this UI matches the application description: {applicationDescription}",
                    "mistral:7b",
                    PromptType.TestApplication,
                    workflowContext);
                
                // Získání odpovědi od LLM
                string response = await _llmClient.GenerateAsync(prompt);

                // Ověření, že odpověď obsahuje očekávaný formát (TEST PASS/FAIL)
                if (!response.Contains("TEST PASS") && !response.Contains("TEST FAIL"))
                {
                    LogMessage("Odpověď LLM nemá očekávaný formát, normalizuji odpověď", MessageSeverity.Warning);
                    
                    // Pokus o normalizaci odpovědi
                    if (response.Contains("pas") || response.Contains("suce") || response.Contains("vyho"))
                    {
                        response = "TEST PASS: " + response;
                    }
                    else if (response.Contains("fail") || response.Contains("nevy") || response.Contains("chyb"))
                    {
                        response = "TEST FAIL: " + response;
                    }
                    else
                    {
                        // Pokud nemůžeme určit výsledek, považujeme test za neúspěšný
                        response = "TEST FAIL: Nepodařilo se určit výsledek testu z odpovědi LLM.";
                    }
                }

                LogMessage("Test dokončen: " + response.Substring(0, Math.Min(response.Length, 100)) + "...");
                return response;
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při testování aplikace: {ex.Message}", MessageSeverity.Error);
                return "TEST FAIL: Došlo k chybě při testování: " + ex.Message;
            }
        }

        /// <summary>
        /// Opravuje problémy nalezené při testování
        /// </summary>
        public async Task<string> RepairTestIssuesAsync(
            string code,
            string testIssues,
            string repairStrategy)
        {
            LogMessage("Opravuji problémy nalezené při testování: " + testIssues.Substring(0, Math.Min(testIssues.Length, 100)) + "...");
            _totalRepairAttempts++;

            try
            {
                // Vytvoření kontextu pro prompt
                var workflowContext = new WorkflowContext
                {
                    CurrentCode = code,
                    TestHistory = testIssues,
                    OperationId = Guid.NewGuid().ToString(),
                    CurrentStage = WorkflowStage.TestRepair,
                    RepairAttempts = 1
                };
                
                // Tvorba promptu pro opravu problémů
                string prompt = await _promptTranslator.TranslateToModelPromptAsync(
                    $"Fix these issues found during testing: {testIssues}",
                    "codellama:13b",
                    PromptType.ErrorRepair,
                    workflowContext);
                
                // Získání odpovědi od LLM
                string response = await _llmClient.GenerateAsync(prompt);

                // Extrakce kódu z odpovědi
                string repairedCode = ExtractCode(response);

                // Ověření, že opravený kód není prázdný nebo příliš krátký
                if (string.IsNullOrWhiteSpace(repairedCode) || repairedCode.Length < 50)
                {
                    LogMessage("Opravený kód je příliš krátký nebo prázdný, vracím původní kód", MessageSeverity.Warning);
                    return code;
                }

                // Ověření, že kód obsahuje základní části
                if (!repairedCode.Contains("public class") || !repairedCode.Contains("Form"))
                {
                    LogMessage("Opravený kód neobsahuje základní části WinForms aplikace, vracím původní kód", MessageSeverity.Warning);
                    return code;
                }

                _successfulRepairs++;
                LogMessage("Kód byl úspěšně opraven na základě výsledků testování");
                return repairedCode;
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při opravě kódu na základě testů: {ex.Message}", MessageSeverity.Error);
                return code; // V případě chyby vracíme původní kód
            }
        }

        /// <summary>
        /// Extracts code block from LLM response
        /// </summary>
        private string ExtractCode(string response)
        {
            // 1. Try to extract code from markdown code block
            var markdownMatch = Regex.Match(response, @"```(?:csharp|cs)?\s*([\s\S]*?)```");
            if (markdownMatch.Success && markdownMatch.Groups.Count > 1)
            {
                string code = markdownMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(code) && code.Length > 50)
                {
                    return code;
                }
            }

            // 2. Try to find C# class definition if no markdown block
            var classMatch = Regex.Match(response, @"(using\s+[^;]+;\s*)+.*?public\s+class\s+\w+.*?{.*}", 
                RegexOptions.Singleline);
            if (classMatch.Success)
            {
                return classMatch.Value.Trim();
            }

            // 3. If nothing else works, remove markdown-like syntax and return the whole response
            return response
                .Replace("```csharp", "")
                .Replace("```cs", "")
                .Replace("```", "")
                .Trim();
        }

        /// <summary>
        /// Předzpracování chybových zpráv pro lepší kontext
        /// </summary>
        private string ProcessErrorMessages(string errors)
        {
            // Pokud je chybová zpráva příliš dlouhá, pokusíme se ji zkrátit a zvýraznit důležité části
            if (errors.Length > 1000)
            {
                // Získání pouze prvních několika chyb (obvykle nejdůležitějších)
                var errorLines = errors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var processedErrors = new List<string>();
                
                int count = 0;
                foreach (var line in errorLines)
                {
                    if (line.Contains("error") || line.Contains("Error") || line.Contains("exception") || line.Contains("Exception"))
                    {
                        processedErrors.Add(line);
                        count++;
                        
                        if (count >= 5) break; // Omezíme na prvních 5 chyb
                    }
                }
                
                return string.Join("\n", processedErrors);
            }
            
            return errors;
        }

        /// <summary>
        /// Vytvoří záložní kód v případě selhání generování
        /// </summary>
        private string CreateFallbackCode(string description)
        {
            LogMessage("Generuji záložní kód", MessageSeverity.Warning);
            
            return @"using System;
using System.Drawing;
using System.Windows.Forms;

namespace GeneratedApp
{
    public class MainForm : Form
    {
        private Button button1;
        private TextBox textBox1;
        private Label label1;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.button1 = new Button();
            this.button1.Location = new Point(100, 100);
            this.button1.Name = ""button1"";
            this.button1.Size = new Size(100, 30);
            this.button1.Text = ""OK"";
            this.button1.Click += new EventHandler(this.button1_Click);
            this.Controls.Add(this.button1);
            
            this.textBox1 = new TextBox();
            this.textBox1.Location = new Point(100, 50);
            this.textBox1.Name = ""textBox1"";
            this.textBox1.Size = new Size(200, 25);
            this.Controls.Add(this.textBox1);
            
            this.label1 = new Label();
            this.label1.Location = new Point(100, 20);
            this.label1.Name = ""label1"";
            this.label1.Size = new Size(200, 23);
            this.label1.Text = """ + description + @""";
            this.Controls.Add(this.label1);
            
            this.ClientSize = new Size(400, 300);
            this.Name = ""MainForm"";
            this.Text = ""Záložní aplikace"";
        }
        
        private void button1_Click(object sender, EventArgs e)
        {
            MessageBox.Show(""Zadali jste: "" + textBox1.Text);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}";
        }

        /// <summary>
        /// Loguje zprávu do konzole a publikuje událost
        /// </summary>
        private void LogMessage(string message, MessageSeverity severity = MessageSeverity.Info)
        {
            var systemMessage = new SystemMessage
            {
                Message = message,
                Severity = severity,
                Timestamp = DateTime.Now,
                Source = "MultiPassCodeGenerator"
            };

            Console.WriteLine($"[{systemMessage.Timestamp:HH:mm:ss}] [{systemMessage.Source}] [{systemMessage.Severity}] {systemMessage.Message}");

            // Publikování události
            _eventBus.Publish(new SystemMessageEvent(systemMessage));
        }
    }
    
    #region Rozšířené podpůrné třídy
    
    /// <summary>
    /// Plán generování aplikace
    /// </summary>
    public class GenerationPlan
    {
        /// <summary>
        /// Popis aplikace
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// Typ aplikace
        /// </summary>
        public ApplicationType ApplicationType { get; set; }
        
        /// <summary>
        /// Seznam hlavních komponent
        /// </summary>
        public List<string> MainComponents { get; set; }
        
        /// <summary>
        /// Detaily jednotlivých komponent
        /// </summary>
        public Dictionary<string, string> ComponentDetails { get; set; }
        
        /// <summary>
        /// Závislosti mezi komponentami
        /// </summary>
        public Dictionary<string, List<string>> DependencyGraph { get; set; }
        
        /// <summary>
        /// Pořadí implementace komponent
        /// </summary>
        public List<string> ImplementationOrder { get; set; }
        
        /// <summary>
        /// Poznámky ke generování
        /// </summary>
        public Dictionary<string, string> GenerationNotes { get; set; }
    }
    
    /// <summary>
    /// Kontext workflow pro generování promptů
    /// </summary>
    public class WorkflowContext
    {
        public string OperationId { get; set; }
        public string Description { get; set; }
        public string DevelopmentPlan { get; set; }
        public string CurrentCode { get; set; }
        public string Errors { get; set; }
        public string FormDescription { get; set; }
        public string TestHistory { get; set; }
        public WorkflowStage CurrentStage { get; set; }
        public int RepairAttempts { get; set; }
        public ApplicationType ApplicationType { get; set; }
    }
    
    /// <summary>
    /// Komponenta aplikace
    /// </summary>
    public class ApplicationComponent
    {
        public string Name { get; set; }
        public ComponentType Type { get; set; }
        public string Description { get; set; }
        public string Functionality { get; set; }
        public string ImplementationPlan { get; set; }
        public string Code { get; set; }
        public List<string> Dependencies { get; set; }
        public int Priority { get; set; } = 999;
        public string GenerationNotes { get; set; }
    }
    
    /// <summary>
    /// Typ komponenty
    /// </summary>
    public enum ComponentType
    {
        UI,
        Logic,
        Data
    }
    
    /// <summary>
    /// Typ zprávy v konverzaci
    /// </summary>
    public enum MessageType
    {
        Prompt,
        Response,
        SystemMessage
    }
    
    /// <summary>
    /// Zpráva v konverzaci
    /// </summary>
    public class ConversationMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public MessageType MessageType { get; set; }
    }
    
    #endregion
    
    #region Události pro multi-pass generování
    
    /// <summary>
    /// Událost publikovaná při započetí generování
    /// </summary>
    public class GenerationStartedEvent
    {
        public string ConversationId { get; }
        public string Description { get; }
        public ApplicationType ApplicationType { get; }

        public GenerationStartedEvent(string conversationId, string description, ApplicationType applicationType)
        {
            ConversationId = conversationId;
            Description = description;
            ApplicationType = applicationType;
        }
    }
    
    /// <summary>
    /// Událost publikovaná při vytvoření plánu
    /// </summary>
    public class PlanCreatedEvent
    {
        public string ConversationId { get; }
        public GenerationPlan Plan { get; }

        public PlanCreatedEvent(string conversationId, GenerationPlan plan)
        {
            ConversationId = conversationId;
            Plan = plan;
        }
    }
    
    /// <summary>
    /// Událost publikovaná při identifikaci komponent
    /// </summary>
    public class ComponentsIdentifiedEvent
    {
        public string ConversationId { get; }
        public List<ApplicationComponent> Components { get; }

        public ComponentsIdentifiedEvent(string conversationId, List<ApplicationComponent> components)
        {
            ConversationId = conversationId;
            Components = components;
        }
    }
    
    /// <summary>
    /// Událost publikovaná při vytvoření implementačního plánu komponenty
    /// </summary>
    public class ImplementationPlanCreatedEvent
    {
        public string ConversationId { get; }
        public string ComponentName { get; }
        public string Plan { get; }

        public ImplementationPlanCreatedEvent(string conversationId, string componentName, string plan)
        {
            ConversationId = conversationId;
            ComponentName = componentName;
            Plan = plan;
        }
    }
    
    /// <summary>
    /// Událost publikovaná při vygenerování kódu komponenty
    /// </summary>
    public class ComponentCodeGeneratedEvent
    {
        public string ConversationId { get; }
        public string ComponentName { get; }
        public int CodeLength { get; }
        public bool IsValid { get; }

        public ComponentCodeGeneratedEvent(string conversationId, string componentName, int codeLength, bool isValid)
        {
            ConversationId = conversationId;
            ComponentName = componentName;
            CodeLength = codeLength;
            IsValid = isValid;
        }
    }
    
    /// <summary>
    /// Událost publikovaná při aktualizaci závislostí
    /// </summary>
    public class DependenciesUpdatedEvent
    {
        public string ConversationId { get; }
        public List<ApplicationComponent> Components { get; }

        public DependenciesUpdatedEvent(string conversationId, List<ApplicationComponent> components)
        {
            ConversationId = conversationId;
            Components = components;
        }
    }
    
    /// <summary>
    /// Událost publikovaná při integraci komponent
    /// </summary>
    public class ComponentsIntegratedEvent
    {
        public string ConversationId { get; }
        public bool Success { get; }
        public string IntegrationType { get; }
        public int ComponentCount { get; }

        public ComponentsIntegratedEvent(string conversationId, bool success, string integrationType, int componentCount)
        {
            ConversationId = conversationId;
            Success = success;
            IntegrationType = integrationType;
            ComponentCount = componentCount;
        }
    }
    
    /// <summary>
    /// Událost publikovaná při dokončení generování
    /// </summary>
    public class GenerationCompletedEvent
    {
        public string ConversationId { get; }
        public string Code { get; }
        public bool Success { get; }
        public string Message { get; }

        public GenerationCompletedEvent(string conversationId, string code, bool success, string message)
        {
            ConversationId = conversationId;
            Code = code;
            Success = success;
            Message = message;
        }
    }
    
    #endregion
}