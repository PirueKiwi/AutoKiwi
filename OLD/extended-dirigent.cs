using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using AutoKiwi.Minimal;
using AutoKiwi.Memory;
using AutoKiwi.Generation;

namespace AutoKiwi.Orchestration
{
    /// <summary>
    /// Dirigent - rozšířená implementace podle PDF dokumentu
    /// Řídící komponenta celého systému s pokročilou rozhodovací logikou
    /// </summary>
    public class Dirigent
    {
        private readonly IEventBus _eventBus;
        private readonly ICodeGenerator _codeGenerator;
        private readonly ICompilationEngine _compilationEngine;
        private readonly IFormAnalyzer _formAnalyzer;
        private readonly IApplicationMemory _appMemory;
        
        // Kompletní přehled o stavu systému
        private WorkflowState _currentState = new WorkflowState { CurrentStage = WorkflowStage.Idle };
        private DirigentOperation _currentOperation;

        // Definované workflow patterns pro různé situace
        private Dictionary<WorkflowType, WorkflowPattern> _workflowPatterns = new Dictionary<WorkflowType, WorkflowPattern>();

        // Sledování úspěšnosti a adaptace
        private SuccessRateTracker _modelPerformanceTracker = new SuccessRateTracker();
        
        private string _generatedCode;
        private Assembly _compiledAssembly;
        private Form _runningForm;
        private string _formDescription;

        /// <summary>
        /// Vytvoří novou instanci Dirigenta
        /// </summary>
        public Dirigent(
            IEventBus eventBus,
            ICodeGenerator codeGenerator,
            ICompilationEngine compilationEngine,
            IFormAnalyzer formAnalyzer,
            IApplicationMemory appMemory)
        {
            _eventBus = eventBus;
            _codeGenerator = codeGenerator;
            _compilationEngine = compilationEngine;
            _formAnalyzer = formAnalyzer;
            _appMemory = appMemory;

            // Inicializace workflow patterns
            InitializeWorkflowPatterns();

            // Přihlášení k událostem
            _eventBus.Subscribe<CompilationCompletedEvent>(OnCompilationCompleted);
            _eventBus.Subscribe<FormAnalysisCompletedEvent>(OnFormAnalysisCompleted);
            _eventBus.Subscribe<ModelSelectionCompletedEvent>(OnModelSelectionCompleted);
            _eventBus.Subscribe<CodeGenerationCompletedEvent>(OnCodeGenerationCompleted);
        }

        /// <summary>
        /// Inicializuje workflow patterns pro různé situace
        /// </summary>
        private void InitializeWorkflowPatterns()
        {
            // Pattern pro standardní WinForms aplikaci
            _workflowPatterns[WorkflowType.StandardWinForms] = new WorkflowPattern
            {
                Name = "Standardní WinForms aplikace",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { Type = WorkflowStepType.Planning, Description = "Analýza požadavků" },
                    new WorkflowStep { Type = WorkflowStepType.DesignGeneration, Description = "Návrh UI" },
                    new WorkflowStep { Type = WorkflowStepType.CodeGeneration, Description = "Generování kódu UI" },
                    new WorkflowStep { Type = WorkflowStepType.CodeGeneration, Description = "Generování business logiky" },
                    new WorkflowStep { Type = WorkflowStepType.Integration, Description = "Integrace UI a logiky" },
                    new WorkflowStep { Type = WorkflowStepType.Compilation, Description = "Kompilace aplikace" },
                    new WorkflowStep { Type = WorkflowStepType.Testing, Description = "Testování aplikace" }
                }
            };

            // Pattern pro opravy chyb
            _workflowPatterns[WorkflowType.ErrorRepair] = new WorkflowPattern
            {
                Name = "Oprava chyb",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { Type = WorkflowStepType.ErrorAnalysis, Description = "Analýza chyb" },
                    new WorkflowStep { Type = WorkflowStepType.CodeRepair, Description = "Oprava kódu" },
                    new WorkflowStep { Type = WorkflowStepType.Compilation, Description = "Kompilace opravené verze" },
                    new WorkflowStep { Type = WorkflowStepType.Testing, Description = "Testování opravené verze" }
                }
            };

            // Pattern pro multi-component generování
            _workflowPatterns[WorkflowType.MultiComponentGeneration] = new WorkflowPattern
            {
                Name = "Multi-komponentní generování",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { Type = WorkflowStepType.Planning, Description = "Rozklad na komponenty" },
                    new WorkflowStep { Type = WorkflowStepType.ComponentPlanning, Description = "Plánování komponent" },
                    new WorkflowStep { Type = WorkflowStepType.ComponentGeneration, Description = "Generování jednotlivých komponent" },
                    new WorkflowStep { Type = WorkflowStepType.Integration, Description = "Integrace komponent" },
                    new WorkflowStep { Type = WorkflowStepType.Compilation, Description = "Kompilace celku" },
                    new WorkflowStep { Type = WorkflowStepType.Testing, Description = "Testování celku" }
                }
            };
        }

        /// <summary>
        /// Spustí vývojovou sekvenci s daným popisem aplikace
        /// </summary>
        public async Task StartDevelopmentSequenceAsync(string applicationDescription)
        {
            try
            {
                _currentOperation = new DirigentOperation
                {
                    ApplicationDescription = applicationDescription,
                    ApplicationType = ApplicationType.WinForms,
                    StartTime = DateTime.Now
                };

                LogMessage("Začínám vývojovou sekvenci");
                UpdateWorkflowState(WorkflowStage.Planning);

                // Určení typu workflow na základě popisu aplikace
                WorkflowType workflowType = DetermineWorkflowType(applicationDescription);
                _currentOperation.WorkflowType = workflowType;
                LogMessage($"Zvolený workflow pattern: {_workflowPatterns[workflowType].Name}");

                // Podle typu workflow postupujeme dál
                await ProcessWorkflowAsync(_currentOperation);
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba: {ex.Message}", MessageSeverity.Error);
                UpdateWorkflowState(WorkflowStage.Error);
                throw;
            }
        }

        /// <summary>
        /// Určí typ workflow na základě popisu aplikace
        /// </summary>
        private WorkflowType DetermineWorkflowType(string applicationDescription)
        {
            // Analýza složitosti aplikace
            bool isComplex = IsComplexApplication(applicationDescription);
            
            if (isComplex)
            {
                return WorkflowType.MultiComponentGeneration;
            }
            
            return WorkflowType.StandardWinForms;
        }

        /// <summary>
        /// Analyzuje, zda je popis aplikace komplexní a vyžaduje multi-komponentní přístup
        /// </summary>
       private bool IsComplexApplication(string description)
{
    // Heuristická analýza složitosti
    int wordCount = description.Split(new[] { ' ', ',', '.', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    
    // Klíčová slova indikující složitost
    string[] complexityKeywords = { "databáze", "database", "multi", "více", "komplexní", "complex", "graf", "chart", "tabulka", "tabulky", "table", "propojení", "connect" };
    
    int keywordCount = complexityKeywords.Count(keyword => description.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
    
    // Vyhodnocení složitosti
    return wordCount > 30 || keywordCount >= 2;
}

        /// <summary>
        /// Zpracuje workflow podle vybraného pattern
        /// </summary>
        private async Task ProcessWorkflowAsync(DirigentOperation operation)
        {
            WorkflowPattern pattern = _workflowPatterns[operation.WorkflowType];
            
            foreach (var step in pattern.Steps)
            {
                LogMessage($"Krok: {step.Description}");
                
                // Zpracování jednotlivých kroků podle typu
                switch (step.Type)
                {
                    case WorkflowStepType.Planning:
                        await AnalyzeRequirementsAsync(operation);
                        break;
                        
                    case WorkflowStepType.DesignGeneration:
                        await GenerateDesignAsync(operation);
                        break;
                        
                    case WorkflowStepType.CodeGeneration:
                        await GenerateInitialCodeAsync(operation);
                        break;
                        
                    case WorkflowStepType.ComponentPlanning:
                        await PlanComponentsAsync(operation);
                        break;
                        
                    case WorkflowStepType.ComponentGeneration:
                        await GenerateComponentsAsync(operation);
                        break;
                        
                    case WorkflowStepType.Integration:
                        await IntegrateComponentsAsync(operation);
                        break;
                        
                    case WorkflowStepType.Compilation:
                        await CompileCodeAsync(operation);
                        break;
                        
                    case WorkflowStepType.Testing:
                        // Testování se spouští asynchronně přes události
                        break;
                        
                    case WorkflowStepType.ErrorAnalysis:
                        // Analýza chyb se provádí v handleru událostí
                        break;
                        
                    case WorkflowStepType.CodeRepair:
                        // Oprava kódu se provádí v handleru událostí
                        break;
                }
            }
        }

        /// <summary>
        /// Rozhoduje o optimálním dalším kroku na základě kontextu
        /// </summary>
        public async Task<WorkflowStep> DetermineNextStepAsync(WorkflowContext context)
        {
            // Analyzuje aktuální stav a kontext
            LogMessage("Určuji další krok na základě kontextu");
            
            if (context.HasCompilationErrors)
            {
                return new WorkflowStep { Type = WorkflowStepType.ErrorAnalysis, Description = "Analýza chyb" };
            }
            
            if (context.HasTestingIssues)
            {
                return new WorkflowStep { Type = WorkflowStepType.CodeRepair, Description = "Oprava problémů z testování" };
            }
            
            if (context.IsGenerationComplete && !context.IsTestingComplete)
            {
                return new WorkflowStep { Type = WorkflowStepType.Testing, Description = "Testování aplikace" };
            }
            
            // Výchozí další krok
            return new WorkflowStep { Type = WorkflowStepType.CodeGeneration, Description = "Generování kódu" };
        }

        /// <summary>
        /// Vybere optimální model pro daný úkol
        /// </summary>
        public async Task<ModelSelectionResult> SelectOptimalModelAsync(TaskType taskType)
        {
            LogMessage($"Vybírám optimální model pro úkol: {taskType}");
            
            // Získání statistik úspěšnosti pro daný typ úkolu
            var modelStats = _modelPerformanceTracker.GetModelStats(taskType);
            
            // Výběr modelu s nejvyšší úspěšností
            string selectedModel = modelStats.OrderByDescending(m => m.SuccessRate).FirstOrDefault()?.ModelId;
            
            // Pokud nemáme statistiky, použijeme výchozí model
            if (string.IsNullOrEmpty(selectedModel))
            {
                switch (taskType)
                {
                    case TaskType.CodeGeneration:
                        selectedModel = "codellama:13b";
                        break;
                    case TaskType.ErrorRepair:
                        selectedModel = "codellama:7b";
                        break;
                    case TaskType.Testing:
                        selectedModel = "mistral:7b";
                        break;
                    default:
                        selectedModel = "codellama:13b";
                        break;
                }
            }
            
            LogMessage($"Vybrán model: {selectedModel}");
            
            // V některých případech zkusíme experimentální model pro zlepšení
            bool tryExperimental = new Random().NextDouble() < 0.1; // 10% šance na experiment
            
            return new ModelSelectionResult
            {
                ModelId = selectedModel,
                TaskType = taskType,
                Confidence = modelStats.FirstOrDefault(m => m.ModelId == selectedModel)?.SuccessRate ?? 0.5,
                IsExperimental = tryExperimental
            };
        }
        
        /// <summary>
        /// Analyzuje požadavky a vytvoří plán vývoje
        /// </summary>
        private async Task AnalyzeRequirementsAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.Planning);
            LogMessage("Analyzuji požadavky");

            // Vybrat optimální model pro plánování
            var modelSelection = await SelectOptimalModelAsync(TaskType.Planning);
            
            // Jednoduchý plán vývoje
            operation.DevelopmentPlan = $"Vytvořit Windows Forms aplikaci s popisem: {operation.ApplicationDescription}";

            // Vyhledání relevantního kontextu z minulých aplikací
            string relevantContext = await _appMemory.GetRelevantContext(operation.ApplicationDescription);
            if (!string.IsNullOrEmpty(relevantContext))
            {
                LogMessage("Nalezeny relevantní příklady v paměti");
                operation.DevelopmentPlan += $"\n\nKontext z paměti:\n{relevantContext}";
            }

            LogMessage($"Plán vytvořen: {operation.DevelopmentPlan}");
        }

        /// <summary>
        /// Generuje návrh UI
        /// </summary>
        private async Task GenerateDesignAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.Design);
            LogMessage("Generuji návrh UI");
            
            // Implementace generování návrhu UI...
            
            LogMessage("Návrh UI vygenerován");
        }

        /// <summary>
        /// Plánuje komponenty pro multi-komponentní generování
        /// </summary>
        private async Task PlanComponentsAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.ComponentPlanning);
            LogMessage("Plánuji komponenty aplikace");
            
            // Rozdělení aplikace na logické komponenty
            operation.Components = new List<ApplicationComponent>
            {
                new ApplicationComponent { Name = "MainForm", Type = ComponentType.UI, Description = "Hlavní formulář aplikace" },
                new ApplicationComponent { Name = "BusinessLogic", Type = ComponentType.Logic, Description = "Business logika aplikace" },
                new ApplicationComponent { Name = "DataAccess", Type = ComponentType.Data, Description = "Přístup k datům" }
            };
            
            foreach (var component in operation.Components)
            {
                LogMessage($"Naplánována komponenta: {component.Name} ({component.Type})");
            }
        }

        /// <summary>
        /// Generuje jednotlivé komponenty pro multi-komponentní přístup
        /// </summary>
        private async Task GenerateComponentsAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.ComponentGeneration);
            LogMessage("Generuji jednotlivé komponenty");
            
            // Generování komponent
            foreach (var component in operation.Components)
            {
                LogMessage($"Generuji komponentu: {component.Name}");
                
                // Výběr optimálního modelu pro daný typ komponenty
                TaskType taskType = ComponentTypeToTaskType(component.Type);
                var modelSelection = await SelectOptimalModelAsync(taskType);
                
                // Generování kódu komponenty
                component.Code = await _codeGenerator.GenerateCodeAsync(
                    $"{component.Description} pro {operation.ApplicationDescription}",
                    operation.DevelopmentPlan,
                    operation.ApplicationType);
                
                LogMessage($"Komponenta {component.Name} vygenerována, {component.Code.Length} znaků");
            }
        }

        /// <summary>
        /// Konvertuje typ komponenty na typ úkolu
        /// </summary>
        private TaskType ComponentTypeToTaskType(ComponentType componentType)
        {
            switch (componentType)
            {
                case ComponentType.UI:
                    return TaskType.UIGeneration;
                case ComponentType.Logic:
                    return TaskType.LogicGeneration;
                case ComponentType.Data:
                    return TaskType.DataGeneration;
                default:
                    return TaskType.CodeGeneration;
            }
        }

        /// <summary>
        /// Integruje komponenty do jednoho celku
        /// </summary>
        private async Task IntegrateComponentsAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.Integration);
            LogMessage("Integruji komponenty");
            
            // Výběr modelu pro integraci
            var modelSelection = await SelectOptimalModelAsync(TaskType.Integration);
            
            // Základní implementace - spojení kódu komponent
            var codeBuilder = new System.Text.StringBuilder();
            
            // Přidání using direktiv
            codeBuilder.AppendLine("using System;");
            codeBuilder.AppendLine("using System.Windows.Forms;");
            codeBuilder.AppendLine("using System.Drawing;");
            codeBuilder.AppendLine("using System.Collections.Generic;");
            codeBuilder.AppendLine("using System.Linq;");
            codeBuilder.AppendLine();
            
            // Přidání namespace
            codeBuilder.AppendLine("namespace GeneratedApp");
            codeBuilder.AppendLine("{");
            
            // Přidání kódu komponent
            foreach (var component in operation.Components)
            {
                // Extrahujeme kód bez using direktiv a namespace deklarací
                string cleanCode = ExtractClassContent(component.Code);
                codeBuilder.AppendLine(cleanCode);
                codeBuilder.AppendLine();
            }
            
            // Uzavření namespace
            codeBuilder.AppendLine("}");
            
            // Uložení integrovaného kódu
            operation.GeneratedCode = codeBuilder.ToString();
            _generatedCode = operation.GeneratedCode;
            
            LogMessage($"Komponenty úspěšně integrovány, celková délka kódu: {_generatedCode.Length} znaků");
        }

        /// <summary>
        /// Extrahuje obsah třídy z kódu
        /// </summary>
        private string ExtractClassContent(string code)
        {
            // Odstraníme using direktivy
            var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var filteredLines = lines.Where(line => !line.TrimStart().StartsWith("using ")).ToList();
            
            // Odstraníme namespace deklarace
            int namespaceStart = -1;
            int namespaceDepth = 0;
            bool inNamespace = false;
            
            for (int i = 0; i < filteredLines.Count; i++)
            {
                string line = filteredLines[i];
                
                if (line.Contains("namespace ") && !inNamespace)
                {
                    namespaceStart = i;
                    inNamespace = true;
                }
                
                if (inNamespace)
                {
                    if (line.Contains("{"))
                    {
                        namespaceDepth++;
                    }
                    
                    if (line.Contains("}"))
                    {
                        namespaceDepth--;
                        
                        if (namespaceDepth == 0)
                        {
                            inNamespace = false;
                            
                            // Extrahujeme obsah namespace
                            var contentLines = filteredLines.Skip(namespaceStart + 1).Take(i - namespaceStart - 1).ToList();
                            return string.Join(Environment.NewLine, contentLines);
                        }
                    }
                }
            }
            
            // Pokud jsme nenašli namespace, vrátíme originální filtrované řádky
            return string.Join(Environment.NewLine, filteredLines);
        }

        /// <summary>
        /// Generuje počáteční kód aplikace
        /// </summary>
        private async Task GenerateInitialCodeAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.Generation);
            LogMessage("Generuji kód");

            // Vybrat optimální model pro generování kódu
            var modelSelection = await SelectOptimalModelAsync(TaskType.CodeGeneration);
            
            // Publikujeme událost o výběru modelu
            _eventBus.Publish(new ModelSelectionCompletedEvent(modelSelection));
            
            // Generování kódu
            _generatedCode = await _codeGenerator.GenerateCodeAsync(
                operation.ApplicationDescription,
                operation.DevelopmentPlan,
                operation.ApplicationType);

            operation.GeneratedCode = _generatedCode;
            
            // Publikujeme událost o vygenerování kódu
            _eventBus.Publish(new CodeGenerationCompletedEvent(operation.GeneratedCode));
            
            LogMessage("Kód vygenerován");
        }

        /// <summary>
        /// Kompiluje vygenerovaný kód
        /// </summary>
        private async Task CompileCodeAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.Compilation);
            LogMessage("Kompiluji kód");

            // Kompilace kódu - vyvolá událost CompilationCompletedEvent
            await _compilationEngine.CompileAsync(operation.GeneratedCode, operation.ApplicationType);
        }

        /// <summary>
        /// Testuje aplikaci - vytvoří instanci formuláře a analyzuje ho
        /// </summary>
        private async Task TestApplicationAsync(DirigentOperation operation, Assembly compiledAssembly)
        {
            UpdateWorkflowState(WorkflowStage.Testing);
            LogMessage("Testuji aplikaci pomocí LLM");

            try
            {
                // Vytvoření instance hlavního formuláře
                _runningForm = CreateFormInstance(compiledAssembly);
                if (_runningForm == null)
                {
                    LogMessage("Nepodařilo se vytvořit instanci formuláře", MessageSeverity.Error);
                    await RepairCompilationErrorsAsync(operation, "Nepodařilo se vytvořit instanci formuláře");
                    return;
                }

                // Publikujeme událost o vytvoření formuláře
                _eventBus.Publish(new FormCreatedEvent(_runningForm));

                // Analýza formuláře pomocí Form2Text pro LLM
                _formDescription = await _formAnalyzer.AnalyzeFormAsync(_runningForm);

                // Publikujeme událost - bude zpracována v OnFormAnalysisCompleted
                _eventBus.Publish(new FormAnalysisCompletedEvent(_formDescription));
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při testování: {ex.Message}", MessageSeverity.Error);
                await RepairCompilationErrorsAsync(operation, ex.Message);
            }
        }

        /// <summary>
        /// Opravuje chyby nalezené při kompilaci
        /// </summary>
        private async Task RepairCompilationErrorsAsync(DirigentOperation operation, string errors)
        {
            UpdateWorkflowState(WorkflowStage.ErrorRepair);
            LogMessage($"Opravuji chyby: {errors}");

            // Vybrat optimální model pro opravy chyb
            var modelSelection = await SelectOptimalModelAsync(TaskType.ErrorRepair);
            
            // Vytvoření strategie opravy
            string repairStrategy = $"Opravit kód s chybami: {errors}";

            // Oprava kódu pomocí LLM
            string repairedCode = await _codeGenerator.RepairCodeAsync(
                operation.GeneratedCode,
                errors,
                repairStrategy);

            // Aktualizace kódu
            operation.GeneratedCode = repairedCode;
            _generatedCode = repairedCode;
            
            // Aktualizace úspěšnosti modelu
            _modelPerformanceTracker.RecordResult(modelSelection.ModelId, TaskType.ErrorRepair, true, 1.0);

            // Znovu zkusíme kompilaci
            operation.RepairAttempts++;
            if (operation.RepairAttempts <= 3)
            {
                await CompileCodeAsync(operation);
            }
            else
            {
                LogMessage("Příliš mnoho pokusů o opravu", MessageSeverity.Error);
                UpdateWorkflowState(WorkflowStage.Error);
            }
        }

        /// <summary>
        /// Finalizuje aplikaci - dokončení procesu a uložení do paměti
        /// </summary>
        private async Task FinalizeApplicationAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.Finalization);
            LogMessage("Finalizuji aplikaci");

            // Uložení aplikace do paměti
            var appProfile = new ApplicationProfile
            {
                Id = Guid.NewGuid().ToString(),
                Name = ExtractAppName(operation.GeneratedCode),
                ApplicationType = operation.ApplicationType,
                Description = operation.ApplicationDescription,
                SourceCode = operation.GeneratedCode,
                CreatedDate = DateTime.Now,
                Features = ExtractApplicationFeatures(operation),
                Tags = GenerateTagsFromDescription(operation.ApplicationDescription)
            };

            _appMemory.SaveApplication(appProfile);

            // Dokončení
            LogMessage("Aplikace úspěšně dokončena a uložena do paměti");
            UpdateWorkflowState(WorkflowStage.Completed);
        }

        /// <summary>
        /// Extrahuje název aplikace z kódu
        /// </summary>
        private string ExtractAppName(string code)
        {
            // Pokus o extrakci názvu třídy formuláře
            var match = Regex.Match(code, @"class\s+(\w+)\s*:\s*Form");
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }

            return $"WinFormsApp_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        /// <summary>
        /// Extrahuje funkce implementované v aplikaci
        /// </summary>
        private List<string> ExtractApplicationFeatures(DirigentOperation operation)
        {
            var features = new List<string>();

            // Základní feature podle typu aplikace
            features.Add("Windows Forms UI");

            // Analýza kódu pro detekci funkcí
            if (operation.GeneratedCode.Contains("Button"))
            {
                features.Add("Interaktivní tlačítka");
            }

            if (operation.GeneratedCode.Contains("TextBox"))
            {
                features.Add("Textové vstupy");
            }

            if (operation.GeneratedCode.Contains("DataGrid"))
            {
                features.Add("Zobrazení dat v tabulce");
            }

            if (operation.GeneratedCode.Contains("MessageBox.Show"))
            {
                features.Add("Dialogová okna");
            }

            if (operation.GeneratedCode.Contains("OpenFileDialog") || operation.GeneratedCode.Contains("SaveFileDialog"))
            {
                features.Add("Práce se soubory");
            }

            if (operation.GeneratedCode.Contains("MenuStrip") || operation.GeneratedCode.Contains("ToolStrip"))
            {
                features.Add("Menu a panely nástrojů");
            }

            // Přidání popisu aplikace jako feature
            features.Add($"Implementace: {operation.ApplicationDescription}");

            return features;
        }

        /// <summary>
        /// Generuje tagy z popisu aplikace
        /// </summary>
        private List<string> GenerateTagsFromDescription(string description)
        {
            // Jednoduchá implementace - rozdělíme popis na slova a odstraníme stopwords
            var stopwords = new HashSet<string> { "a", "an", "the", "and", "or", "but", "is", "are", "s", "pro", "do", "na", "v", "s", "z", "k", "o", "u" };
            
            var words = description.ToLower()
                .Split(new[] { ' ', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length > 2) // Jen slova delší než 2 znaky
                .Where(word => !stopwords.Contains(word)) // Odstraníme stopwords
                .Distinct() // Odstraníme duplicity
                .Take(10) // Max 10 tagů
                .ToList();
            
            return words;
        }

        /// <summary>
        /// Vytváří instanci formuláře z assembly
        /// </summary>
        private Form CreateFormInstance(Assembly assembly)
        {
            try
            {
                // Hledání třídy formuláře
                var formType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(Form).IsAssignableFrom(t));

                if (formType == null)
                {
                    LogMessage("V assembly nebyl nalezen žádný typ Form", MessageSeverity.Error);
                    return null;
                }

                // Vytvoření instance
                return (Form)Activator.CreateInstance(formType);
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při vytváření instance formuláře: {ex.Message}", MessageSeverity.Error);
                return null;
            }
        }

        /// <summary>
        /// Aktualizuje stav workflow a publikuje událost
        /// </summary>
        private void UpdateWorkflowState(WorkflowStage newStage)
        {
            _currentState.PreviousStage = _currentState.CurrentStage;
            _currentState.CurrentStage = newStage;
            _currentState.StageStartTime = DateTime.Now;

            // Publikování události
            _eventBus.Publish(new WorkflowStateChangedEvent(_currentState));
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
                Source = "Dirigent"
            };

            Console.WriteLine($"[{systemMessage.Timestamp:HH:mm:ss}] [{systemMessage.Source}] [{systemMessage.Severity}] {systemMessage.Message}");

            // Publikování události
            _eventBus.Publish(new SystemMessageEvent(systemMessage));
        }

        #region Event Handlers
        /// <summary>
        /// Přidá zprávu do konverzační historie a publikuje ji jako událost
        /// </summary>
        
        /// <summary>
        /// Vybere relevantní zprávy z historie pro udržení kontextu
        /// </summary>
        private List<ConversationMessage> SelectRelevantMessages(List<ConversationMessage> allMessages)
        {
            var selectedMessages = new List<ConversationMessage>();
            
            // 1. Přidání prvních zpráv pro úvodní kontext
            if (allMessages.Count > 0)
            {
                selectedMessages.Add(allMessages.First());
            }
            
            // 2. Přidání zpráv o změnách stavu workflow
            var workflowMessages = allMessages
                .Where(m => m.MessageType == MessageType.SystemMessage && 
                       m.Content.Contains("Workflow") || 
                       m.Content.Contains("Stage"))
                .Take(3);
                
            selectedMessages.AddRange(workflowMessages);
            
            // 3. Přidání zpráv s chybami
            var errorMessages = allMessages
                .Where(m => m.MessageType == MessageType.SystemMessage && 
                       (m.Content.Contains("Error") || 
                        m.Content.Contains("Chyba") || 
                        m.Content.Contains("Failed") || 
                        m.Content.Contains("Selhalo")))
                .Take(3);
                
            selectedMessages.AddRange(errorMessages);
            
            // 4. Přidání posledních několika zpráv pro aktuální kontext
            var recentMessages = allMessages
                .Skip(Math.Max(0, allMessages.Count - 5))
                .Take(5);
                
            selectedMessages.AddRange(recentMessages);
            
            // 5. Odstraníme duplicitní zprávy a seřadíme podle času
            return selectedMessages
                .GroupBy(m => m.Timestamp)
                .Select(g => g.First())
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Handler události CompilationCompletedEvent
        /// </summary>
        private async void OnCompilationCompleted(CompilationCompletedEvent evt)
        {
            if (evt.Success)
            {
                LogMessage("Kompilace úspěšná");
                _compiledAssembly = evt.CompiledAssembly;

                // Pokud byla kompilace úspěšná, pokračujeme testováním
                await TestApplicationAsync(_currentOperation, evt.CompiledAssembly);
            }
            else
            {
                LogMessage($"Kompilace selhala: {evt.Errors}", MessageSeverity.Error);

                // Oprava chyb
                await RepairCompilationErrorsAsync(_currentOperation, evt.Errors);
            }
        }

        /// <summary>
        /// Handler události FormAnalysisCompletedEvent
        /// </summary>
        private async void OnFormAnalysisCompleted(FormAnalysisCompletedEvent evt)
        {
            LogMessage("Analýza formuláře dokončena");

            // Vybrat optimální model pro testování
            var modelSelection = await SelectOptimalModelAsync(TaskType.Testing);

            // LLM TESTUJE APLIKACI - tento krok je zásadní pro autonomní systém
            string testResult = await _codeGenerator.TestApplicationAsync(
                _formDescription,
                _currentOperation.ApplicationDescription);

            // Vyhodnocení výsledků testování
            if (testResult.Contains("TEST PASS") || testResult.Contains("SUCCESS"))
            {
                LogMessage("Testování úspěšné: " + testResult);
                
                // Aktualizace úspěšnosti modelu
                _modelPerformanceTracker.RecordResult(modelSelection.ModelId, TaskType.Testing, true, 1.0);
                
                await FinalizeApplicationAsync(_currentOperation);
            }
            else
            {
                LogMessage("Testování odhalilo problémy: " + testResult, MessageSeverity.Warning);
                
                // Aktualizace úspěšnosti modelu
                _modelPerformanceTracker.RecordResult(modelSelection.ModelId, TaskType.Testing, false, 0.0);

                // Oprava problémů nalezených při testování
                UpdateWorkflowState(WorkflowStage.TestRepair);

                // Vybrat optimální model pro opravy
                var repairModelSelection = await SelectOptimalModelAsync(TaskType.ErrorRepair);

                string repairedCode = await _codeGenerator.RepairTestIssuesAsync(
                    _generatedCode,
                    testResult,
                    "Opravit problémy nalezené při testování");

                _currentOperation.GeneratedCode = repairedCode;
                _generatedCode = repairedCode;

                // Zavření starého formuláře
                if (_runningForm != null && !_runningForm.IsDisposed)
                {
                    _runningForm.Close();
                    _runningForm.Dispose();
                }

                // Znovu kompilace a test
                await CompileCodeAsync(_currentOperation);
            }
        }

        /// <summary>
        /// Handler události ModelSelectionCompletedEvent
        /// </summary>
        private void OnModelSelectionCompleted(ModelSelectionCompletedEvent evt)
        {
            LogMessage($"Model vybrán: {evt.Selection.ModelId} pro úkol {evt.Selection.TaskType} (důvěra: {evt.Selection.Confidence:P0})");
        }

        /// <summary>
        /// Handler události CodeGenerationCompletedEvent
        /// </summary>
        private void OnCodeGenerationCompleted(CodeGenerationCompletedEvent evt)
        {
            LogMessage($"Kód vygenerován, délka: {evt.GeneratedCode.Length} znaků");
        }

        #endregion
    }

    #region Podpůrné třídy a rozhraní

    /// <summary>
    /// Operace prováděná Dirigentem
    /// </summary>
    public class DirigentOperation
    {
        public string ApplicationDescription { get; set; }
        public ApplicationType ApplicationType { get; set; }
        public WorkflowType WorkflowType { get; set; }
        public string DevelopmentPlan { get; set; }
        public string GeneratedCode { get; set; }
        public DateTime StartTime { get; set; }
        public int RepairAttempts { get; set; }
        public List<ApplicationComponent> Components { get; set; } = new List<ApplicationComponent>();
    }

    /// <summary>
    /// Stav workflow procesu
    /// </summary>
    public class WorkflowState
    {
        public WorkflowStage CurrentStage { get; set; }
        public WorkflowStage PreviousStage { get; set; }
        public DateTime StageStartTime { get; set; }

        public WorkflowState Clone()
        {
            return new WorkflowState
            {
                CurrentStage = this.CurrentStage,
                PreviousStage = this.PreviousStage,
                StageStartTime = this.StageStartTime
            };
        }
    }

    /// <summary>
    /// Fáze workflow procesu
    /// </summary>
    public enum WorkflowStage
    {
        Idle,
        Planning,
        Design,
        Generation,
        ComponentPlanning,
        ComponentGeneration,
        Integration,
        Compilation,
        ErrorRepair,
        Testing,
        TestRepair,
        Finalization,
        Completed,
        Error
    }

    /// <summary>
    /// Typ workflow
    /// </summary>
    public enum WorkflowType
    {
        StandardWinForms,
        ErrorRepair,
        MultiComponentGeneration
    }

    /// <summary>
    /// Typ kroku workflow
    /// </summary>
    public enum WorkflowStepType
    {
        Planning,
        DesignGeneration,
        CodeGeneration,
        ComponentPlanning,
        ComponentGeneration,
        Integration,
        Compilation,
        Testing,
        ErrorAnalysis,
        CodeRepair
    }

    /// <summary>
    /// Krok workflow
    /// </summary>
    public class WorkflowStep
    {
        public WorkflowStepType Type { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Pattern workflow
    /// </summary>
    public class WorkflowPattern
    {
        public string Name { get; set; }
        public List<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    }

    /// <summary>
    /// Kontext workflow pro rozhodování
    /// </summary>
    public class WorkflowContext
    {
        public bool HasCompilationErrors { get; set; }
        public bool HasTestingIssues { get; set; }
        public bool IsGenerationComplete { get; set; }
        public bool IsTestingComplete { get; set; }
        public ApplicationType ApplicationType { get; set; }
        public WorkflowType WorkflowType { get; set; }
    }

    /// <summary>
    /// Typ úkolu pro výběr modelu
    /// </summary>
    public enum TaskType
    {
        Planning,
        CodeGeneration,
        UIGeneration,
        LogicGeneration,
        DataGeneration,
        Integration,
        ErrorRepair,
        Testing
    }

    /// <summary>
    /// Výsledek výběru modelu
    /// </summary>
    public class ModelSelectionResult
    {
        public string ModelId { get; set; }
        public TaskType TaskType { get; set; }
        public double Confidence { get; set; }
        public bool IsExperimental { get; set; }
    }

    /// <summary>
    /// Komponenta aplikace
    /// </summary>
    public class ApplicationComponent
    {
        public string Name { get; set; }
        public ComponentType Type { get; set; }
        public string Description { get; set; }
        public string Code { get; set; }
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
    /// Statistika úspěšnosti modelu
    /// </summary>
    public class ModelStats
    {
        public string ModelId { get; set; }
        public TaskType TaskType { get; set; }
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public double SuccessRate => TotalAttempts > 0 ? (double)SuccessfulAttempts / TotalAttempts : 0;
    }

    /// <summary>
    /// Tracker úspěšnosti modelů
    /// </summary>
    public class SuccessRateTracker
    {
        private readonly Dictionary<string, Dictionary<TaskType, ModelStats>> _modelStats = new Dictionary<string, Dictionary<TaskType, ModelStats>>();

        /// <summary>
        /// Zaznamenává výsledek použití modelu
        /// </summary>
        public void RecordResult(string modelId, TaskType taskType, bool success, double confidence)
        {
            if (!_modelStats.ContainsKey(modelId))
            {
                _modelStats[modelId] = new Dictionary<TaskType, ModelStats>();
            }

            if (!_modelStats[modelId].ContainsKey(taskType))
            {
                _modelStats[modelId][taskType] = new ModelStats
                {
                    ModelId = modelId,
                    TaskType = taskType
                };
            }

            var stats = _modelStats[modelId][taskType];
            stats.TotalAttempts++;
            if (success)
            {
                stats.SuccessfulAttempts++;
            }
        }

        /// <summary>
        /// Získá statistiky všech modelů pro daný typ úkolu
        /// </summary>
        public List<ModelStats> GetModelStats(TaskType taskType)
        {
            var result = new List<ModelStats>();

            foreach (var modelEntry in _modelStats)
            {
                if (modelEntry.Value.TryGetValue(taskType, out var stats))
                {
                    result.Add(stats);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Událost publikovaná při výběru modelu
    /// </summary>
    public class ModelSelectionCompletedEvent
    {
        public ModelSelectionResult Selection { get; }

        public ModelSelectionCompletedEvent(ModelSelectionResult selection)
        {
            Selection = selection;
        }
    }

    /// <summary>
    /// Událost publikovaná při vygenerování kódu
    /// </summary>
    public class CodeGenerationCompletedEvent
    {
        public string GeneratedCode { get; }

        public CodeGenerationCompletedEvent(string generatedCode)
        {
            GeneratedCode = generatedCode;
        }
    }

    #endregion
}