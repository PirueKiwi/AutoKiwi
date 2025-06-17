using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoKiwi.Generation;
using AutoKiwi.Minimal;
using AutoKiwi.ContextManagement;
using AutoKiwi; // Ensure this is included

namespace AutoKiwi.Translation
{
    /// <summary>
    /// Překladač promptů - Implementace podle PDF
    /// Překládá generické instrukce do formátu optimálního pro konkrétní model
    /// </summary>
    public class PromptTranslator
    {
        private readonly IEventBus _eventBus;
        private readonly ContextManager _contextManager;
        private readonly IApplicationMemory _appMemory;

        // Modelově specifické šablony promptů
        private Dictionary<string, Dictionary<PromptType, PromptTemplate>> _modelPromptTemplates;

        // Statistiky úspěšnosti promptů
        private Dictionary<string, Dictionary<PromptType, PromptSuccessRate>> _promptSuccessRates;

        /// <summary>
        /// Vytvoří novou instanci PromptTranslator
        /// </summary>
        public PromptTranslator(
            IEventBus eventBus,
            ContextManager contextManager,
            IApplicationMemory appMemory)
        {
            _eventBus = eventBus;
            _contextManager = contextManager;
            _appMemory = appMemory;

            InitializeTemplates();
            InitializeSuccessRates();

            // Přihlásíme se k událostem pro adaptivní učení
            _eventBus.Subscribe<CompilationCompletedEvent>(OnCompilationCompleted);
            _eventBus.Subscribe<FormAnalysisCompletedEvent>(OnFormAnalysisCompleted);
        }

        /// <summary>
        /// Inicializuje šablony promptů pro různé modely
        /// </summary>
        private void InitializeTemplates()
        {
            _modelPromptTemplates = new Dictionary<string, Dictionary<PromptType, PromptTemplate>>();

            // Šablony pro CodeLlama modely
            var codeLlamaTemplates = new Dictionary<PromptType, PromptTemplate>();

            codeLlamaTemplates[PromptType.CodeGeneration] = new PromptTemplate
            {
                Template = @"<instructions>
You are an expert C# developer specializing in Windows Forms applications.
Your task is to create a complete, working Windows Forms application based on the following requirements.

Requirements:
{Description}

Development Plan:
{DevelopmentPlan}

{MemoryContext}

Important guidelines:
- Use System.Windows.Forms and System.Drawing
- Create a clean, well-structured application
- Include all event handlers
- Make sure the code is fully functional
- Include a Main method with the [STAThread] attribute
- Use consistent naming conventions
- The namespace should be GeneratedApp
- The main form class should inherit from Form

Return ONLY the complete C# code without explanations.
</instructions>",
                Tokens = 350,
                TokenRatio = 1.0,
                SuccessRate = 0.8
            };

            codeLlamaTemplates[PromptType.ErrorRepair] = new PromptTemplate
            {
                Template = @"<instructions>
You are an expert C# debugger. Fix the following code that has compilation errors:

Error details:
{Errors}

Code to fix:{Code}
Provide ONLY the fixed code without explanations. Make minimal changes necessary to fix the issues.
</instructions>",
                Tokens = 250,
                TokenRatio = 0.5,
                SuccessRate = 0.85
            };

            codeLlamaTemplates[PromptType.TestApplication] = new PromptTemplate
            {
                Template = @"<instructions>
You are a QA specialist testing a Windows Forms application.

Original application requirements:
{Description}

Current application UI:
{FormDescription}

Evaluate if the application meets the requirements. Check for:
1. All required functionality is present
2. The UI is appropriate and user-friendly
3. The controls have proper names and labels
4. The layout is logical and well-organized

Return your evaluation in this format:
- If the application meets requirements: TEST PASS: [reasons]
- If the application has issues: TEST FAIL: [detailed list of issues]
</instructions>",
                Tokens = 300,
                TokenRatio = 0.7,
                SuccessRate = 0.75
            };

            // Šablony pro Mistral modely
            var mistralTemplates = new Dictionary<PromptType, PromptTemplate>();

            mistralTemplates[PromptType.CodeGeneration] = new PromptTemplate
            {
                Template = @"You will create a complete C# Windows Forms application based on this description:

DESCRIPTION:
{Description}

PLAN:
{DevelopmentPlan}

CONTEXT:
{MemoryContext}

REQUIREMENTS:
- Include all necessary using statements
- Use Windows Forms and System.Drawing
- Create all required controls and event handlers
- Make code compilable and runnable
- Include Main method with [STAThread]
- Use namespace GeneratedApp

CODE ONLY, NO EXPLANATIONS:",
                Tokens = 320,
                TokenRatio = 1.1,
                SuccessRate = 0.75
            };

            mistralTemplates[PromptType.ErrorRepair] = new PromptTemplate
            {
                Template = @"Fix these C# errors:

ERRORS:
{Errors}

CODE:{Code}
FIXED CODE ONLY:",
                Tokens = 200,
                TokenRatio = 0.4,
                SuccessRate = 0.8
            };

            mistralTemplates[PromptType.TestApplication] = new PromptTemplate
            {
                Template = @"Test this Windows Forms application:

REQUIREMENTS:
{Description}

APPLICATION INTERFACE:
{FormDescription}

Check if all requirements are met. Test functionality and usability.

Reply with either:
TEST PASS: [reasons]
or
TEST FAIL: [issues]",
                Tokens = 250,
                TokenRatio = 0.6,
                SuccessRate = 0.85
            };

            // Registrace šablon pro jednotlivé modely
            _modelPromptTemplates["codellama:13b"] = codeLlamaTemplates;
            _modelPromptTemplates["codellama:7b"] = codeLlamaTemplates;
            _modelPromptTemplates["mistral:7b"] = mistralTemplates;

            // Výchozí šablony pro neznámé modely
            _modelPromptTemplates["default"] = codeLlamaTemplates;
        }

        /// <summary>
        /// Inicializuje statistiky úspěšnosti promptů
        /// </summary>
        private void InitializeSuccessRates()
        {
            _promptSuccessRates = new Dictionary<string, Dictionary<PromptType, PromptSuccessRate>>();

            foreach (var modelId in _modelPromptTemplates.Keys)
            {
                _promptSuccessRates[modelId] = new Dictionary<PromptType, PromptSuccessRate>();

                foreach (var promptType in _modelPromptTemplates[modelId].Keys)
                {
                    _promptSuccessRates[modelId][promptType] = new PromptSuccessRate
                    {
                        TotalAttempts = 0,
                        SuccessfulAttempts = 0
                    };
                }
            }
        }

        /// <summary>
        /// Překládá požadavek na prompt pro konkrétní model
        /// </summary>
        public async Task<string> TranslateToModelPromptAsync(
            string humanIntent,
            string modelId,
            PromptType promptType,
            WorkflowContext context)
        {
            LogMessage($"Překládám požadavek na prompt typu {promptType} pro model {modelId}");

            // Pokud nemáme šablony pro daný model, použijeme výchozí
            if (!_modelPromptTemplates.ContainsKey(modelId))
            {
                modelId = "default";
            }

            // Získání šablony
            var templates = _modelPromptTemplates[modelId];
            if (!templates.ContainsKey(promptType))
            {
                LogMessage($"Chybí šablona pro typ promptu {promptType}, používám CodeGeneration", MessageSeverity.Warning);
                promptType = PromptType.CodeGeneration;
            }

            var template = templates[promptType];

            // Sestavení kontextu ze současného stavu a historie
            string conversationId = context.OperationId;
            var contextData = await _contextManager.GetContextDataAsync(conversationId, ContextScope.Relevant);

            // Zjistíme relevantní kontext z paměti aplikací
            string memoryContext = string.Empty;
            if (promptType == PromptType.CodeGeneration)
            {
                memoryContext = await _appMemory.GetRelevantContext(humanIntent);
            }

            // Sestavení parametrů pro prompt
            var promptParams = new Dictionary<string, string>
            {
                ["Description"] = humanIntent,
                ["DevelopmentPlan"] = context.DevelopmentPlan ?? string.Empty,
                ["MemoryContext"] = memoryContext,
                ["Code"] = context.CurrentCode ?? string.Empty,
                ["Errors"] = context.Errors ?? string.Empty,
                ["FormDescription"] = context.FormDescription ?? string.Empty,
                ["ConversationContext"] = contextData ?? string.Empty
            };

            // Nahrazení parametrů v šabloně
            string prompt = template.Template;
            foreach (var param in promptParams)
            {
                prompt = prompt.Replace($"{{{param.Key}}}", param.Value);
            }

            // Adaptace promptu na základě kontextu
            prompt = AdaptPromptBasedOnContext(prompt, promptType, context);

            // Zaznamenání použití promptu
            await _contextManager.AddMessageAsync(
                conversationId,
                new AutoKiwi.ConversationMessage
                {
                    Role = "system",
                    Content = $"Prompt type: {promptType}, model: {modelId}",
                    Timestamp = DateTime.Now,
                    MessageType = MessageType.Prompt
                });

            LogMessage($"Přeloženo na prompt ({prompt.Length} znaků)");

            return prompt;
        }

        /// <summary>
        /// Adaptuje prompt na základě kontextu
        /// </summary>
        private string AdaptPromptBasedOnContext(string prompt, PromptType promptType, WorkflowContext context)
        {
            // Rozšíření promptu podle typu a kontextu
            if (promptType == PromptType.ErrorRepair && context.RepairAttempts > 1)
            {
                // Pro opakované pokusy o opravu přidáme dodatečné instrukce
                prompt += $"\n\nNote: This is attempt #{context.RepairAttempts} to fix this code. Previous attempts have failed. Focus on comprehensive error fixing.";
            }

            if (promptType == PromptType.TestApplication && !string.IsNullOrEmpty(context.TestHistory))
            {
                // Pro testování přidáme historii předchozích testů
                prompt += $"\n\nPrevious test results: {context.TestHistory}";
            }

            // Adaptace délky promptu podle složitosti úkolu
            bool isComplex = IsComplexTask(context);
            if (isComplex)
            {
                // Pro složité úkoly přidáme dodatečné instrukce
                prompt += "\n\nThis is a complex task requiring careful consideration. Take your time to ensure all parts are properly implemented and integrated.";
            }

            return prompt;
        }

        /// <summary>
        /// Vyhodnotí, zda je úkol složitý
        /// </summary>
        private bool IsComplexTask(WorkflowContext context)
        {
            // Jednoduché heuristiky pro určení složitosti
            if (!string.IsNullOrEmpty(context.Description) && context.Description.Length > 200)
            {
                return true;
            }

            if (context.RepairAttempts > 2)
            {
                return true;
            }

            if (context.ApplicationType == ApplicationType.WinForms &&
                !string.IsNullOrEmpty(context.CurrentCode) &&
                context.CurrentCode.Length > 1000)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Překládá výstup modelu do formátu vhodného pro uživatele
        /// </summary>
        public async Task<HumanReadableOutput> TranslateModelOutputAsync(
            string modelOutput,
            OutputIntention intention)
        {
            LogMessage($"Překládám výstup modelu (délka: {modelOutput.Length})");

            var output = new HumanReadableOutput
            {
                OriginalOutput = modelOutput,
                ProcessedOutput = modelOutput,
                Intention = intention
            };

            // Dodatečné zpracování podle typu výstupu
            switch (intention)
            {
                case OutputIntention.CodeGeneration:
                    output.ProcessedOutput = CleanupGeneratedCode(modelOutput);
                    break;

                case OutputIntention.ErrorRepair:
                    output.ProcessedOutput = ExtractRepairedCode(modelOutput);
                    break;

                case OutputIntention.TestResult:
                    output.ProcessedOutput = FormatTestResult(modelOutput);
                    output.IsSuccess = modelOutput.Contains("TEST PASS") || modelOutput.Contains("SUCCESS");
                    break;
            }

            return output;
        }

        /// <summary>
        /// Vyčistí vygenerovaný kód od komentářů a instrukcí
        /// </summary>
        private string CleanupGeneratedCode(string code)
        {
            // Odstranění markdown formátování
            var codeWithoutMarkdown = RemoveMarkdownFormatting(code);

            // Odstranění komentářů před a za kódem
            var lines = codeWithoutMarkdown.Split(new[] { '\r', '\n' }, StringSplitOptions.None);

            int startLine = 0;
            int endLine = lines.Length - 1;

            // Najdeme první řádek s kódem
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("using ") || lines[i].Contains("namespace ") || lines[i].Contains("public class "))
                {
                    startLine = i;
                    break;
                }
            }

            // Najdeme poslední řádek s kódem
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].Contains("}"))
                {
                    endLine = i;
                    break;
                }
            }

            // Extrahujeme pouze kód
            var codeLines = lines.Skip(startLine).Take(endLine - startLine + 1);
            return string.Join(Environment.NewLine, codeLines);
        }

        /// <summary>
        /// Extrahuje opravený kód z odpovědi modelu
        /// </summary>
        private string ExtractRepairedCode(string output)
        {
            return RemoveMarkdownFormatting(output);
        }

        /// <summary>
        /// Formátuje výsledek testování
        /// </summary>
        private string FormatTestResult(string output)
        {
            // Extrakce klíčových informací z testu
            if (output.Contains("TEST PASS"))
            {
                var passIndex = output.IndexOf("TEST PASS");
                return output.Substring(passIndex);
            }
            else if (output.Contains("TEST FAIL"))
            {
                var failIndex = output.IndexOf("TEST FAIL");
                return output.Substring(failIndex);
            }

            return output;
        }

        /// <summary>
        /// Odstraní markdown formátování z kódu
        /// </summary>
        private string RemoveMarkdownFormatting(string text)
        {
            // Odstranění bloků kódu
            var result = text;

            if (result.Contains("```csharp") || result.Contains("```cs"))
            {
                int start = result.IndexOf("```");
                if (start >= 0)
                {
                    start = result.IndexOf('\n', start) + 1;
                    int end = result.IndexOf("```", start);
                    if (end >= 0)
                    {
                        result = result.Substring(start, end - start);
                    }
                }
            }
            else if (result.Contains("```"))
            {
                int start = result.IndexOf("```") + 3;
                int end = result.LastIndexOf("```");
                if (end > start)
                {
                    result = result.Substring(start, end - start);
                }
            }

            return result.Trim();
        }

        /// <summary>
        /// Zaznamenává úspěšnost promptu
        /// </summary>
        public void RecordPromptSuccess(string modelId, PromptType promptType, bool success)
        {
            if (!_promptSuccessRates.ContainsKey(modelId))
            {
                _promptSuccessRates[modelId] = new Dictionary<PromptType, PromptSuccessRate>();
            }

            if (!_promptSuccessRates[modelId].ContainsKey(promptType))
            {
                _promptSuccessRates[modelId][promptType] = new PromptSuccessRate
                {
                    TotalAttempts = 0,
                    SuccessfulAttempts = 0
                };
            }

            var stats = _promptSuccessRates[modelId][promptType];
            stats.TotalAttempts++;
            if (success)
            {
                stats.SuccessfulAttempts++;
            }

            // Aktualizace úspěšnosti šablony
            if (_modelPromptTemplates.ContainsKey(modelId) && _modelPromptTemplates[modelId].ContainsKey(promptType))
            {
                var template = _modelPromptTemplates[modelId][promptType];
                template.SuccessRate = stats.SuccessfulAttempts / (double)stats.TotalAttempts;
            }

            LogMessage($"Zaznamenána úspěšnost promptu {promptType} pro model {modelId}: {(success ? "úspěch" : "neúspěch")}");
        }

        /// <summary>
        /// Event handler pro událost CompilationCompletedEvent
        /// </summary>
        private void OnCompilationCompleted(CompilationCompletedEvent evt)
        {
            // Zaznamenáváme úspěšnost promptu pro generování kódu
            // Removed reference to evt.ModelId as it does not exist in CompilationCompletedEvent
            RecordPromptSuccess("default", PromptType.CodeGeneration, evt.Success);

            if (!evt.Success)
            {
                // Pro neúspěšnou kompilaci zaznamenáváme selhání
                RecordPromptSuccess("default", PromptType.ErrorRepair, false);
            }
        }

        /// <summary>
        /// Event handler pro událost FormAnalysisCompletedEvent
        /// </summary>
        private void OnFormAnalysisCompleted(FormAnalysisCompletedEvent evt)
        {
            // Zde bychom mohli zaznamenat úspěšnost testování, ale potřebujeme výsledek testu
            // Ten přijde později v jiné události
        }

        /// <summary>
        /// Loguje zprávu
        /// </summary>
        private void LogMessage(string message, MessageSeverity severity = MessageSeverity.Info)
        {
            var systemMessage = new SystemMessage
            {
                Message = message,
                Severity = severity,
                Timestamp = DateTime.Now,
                Source = "PromptTranslator"
            };

            Console.WriteLine($"[{systemMessage.Timestamp:HH:mm:ss}] [{systemMessage.Source}] [{systemMessage.Severity}] {systemMessage.Message}");

            // Publikování události
            _eventBus.Publish(new SystemMessageEvent(systemMessage));
        }
    }

    /// <summary>
    /// Typ promptu
    /// </summary>
    public enum PromptType
    {
        CodeGeneration,
        ErrorRepair,
        TestApplication,
        ContextAnalysis,
        Planning
    }

    /// <summary>
    /// Záměr výstupu
    /// </summary>
    public enum OutputIntention
    {
        CodeGeneration,
        ErrorRepair,
        TestResult,
        PlanningResult
    }

    /// <summary>
    /// Šablona promptu
    /// </summary>
    public class PromptTemplate
    {
        public string Template { get; set; }
        public int Tokens { get; set; }
        public double TokenRatio { get; set; }
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// Statistika úspěšnosti promptu
    /// </summary>
    public class PromptSuccessRate
    {
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public double SuccessRate => TotalAttempts > 0 ? (double)SuccessfulAttempts / TotalAttempts : 0;
    }

    /// <summary>
    /// Výstup přeložený pro uživatele
    /// </summary>
    public class HumanReadableOutput
    {
        public string OriginalOutput { get; set; }
        public string ProcessedOutput { get; set; }
        public OutputIntention Intention { get; set; }
        public bool IsSuccess { get; set; }
    }

    /// <summary>
    /// Rozsah kontextu
    /// </summary>
    public enum ContextScope
    {
        Current,
        Recent,
        Relevant,
        Complete
    }
}