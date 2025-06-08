// File: AdaptiveLearningModel.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutoKiwi.Adaptive
{
    /// <summary>
    /// Adaptivní model učení - učí se z úspěšných a neúspěšných pokusů
    /// </summary>
    public class AdaptiveLearningModel
    {
        private readonly IEventBus _eventBus;
        private readonly ContextManager _contextManager;
        private readonly Dictionary<string, ModelStats> _modelStats = new Dictionary<string, ModelStats>();
        private readonly Dictionary<string, PromptStats> _promptStats = new Dictionary<string, PromptStats>();
        private readonly Dictionary<string, StrategyStats> _strategyStats = new Dictionary<string, StrategyStats>();

        // Parametry adaptivního učení
        private readonly double _learningRate = 0.2;
        private readonly double _explorationRate = 0.1;
        private readonly double _decayRate = 0.95;

        /// <summary>
        /// Konstruktor adaptivního modelu učení
        /// </summary>
        public AdaptiveLearningModel(IEventBus eventBus, ContextManager contextManager)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));

            RegisterDefaultModels();
            RegisterDefaultPrompts();
            RegisterDefaultStrategies();

            // Registrace k událostem
            _eventBus.Subscribe<CompilationCompletedEvent>(OnCompilationCompleted);
            _eventBus.Subscribe<FormAnalysisCompletedEvent>(OnFormAnalysisCompleted);
        }

        /// <summary>
        /// Vybere optimální model pro daný úkol
        /// </summary>
        public async Task<ModelSelectionResult> SelectModelAsync(TaskType taskType, ModelSelectionContext context)
        {
            LogMessage($"Vybírám optimální model pro úkol: {taskType}");

            // Zjistíme, zda máme jít cestou experimentu
            bool explore = ShouldExplore();

            if (explore)
            {
                // Experimentální výběr - zkoušíme náhodně některý model
                return SelectExperimentalModel(taskType);
            }
            else
            {
                // Běžný výběr - vybíráme nejlepší model podle statistik
                return SelectBestModel(taskType, context);
            }
        }

        /// <summary>
        /// Vybere prompt pro daný typ úkolu
        /// </summary>
        public async Task<string> SelectPromptTypeAsync(PromptType promptType, PromptSelectionContext context)
        {
            LogMessage($"Vybírám vhodný prompt pro typ: {promptType}");

            // Buď experimentální výběr, nebo nejlepší známý prompt
            if (ShouldExplore())
            {
                return SelectExperimentalPrompt(promptType);
            }
            else
            {
                return SelectBestPrompt(promptType, context);
            }
        }

        /// <summary>
        /// Vybere vhodnou strategii pro danou situaci
        /// </summary>
        public async Task<string> SelectStrategyAsync(string situation, StrategySelectionContext context)
        {
            LogMessage($"Vybírám vhodnou strategii pro situaci: {situation}");

            if (ShouldExplore())
            {
                return SelectExperimentalStrategy(situation);
            }
            else
            {
                return SelectBestStrategy(situation, context);
            }
        }

        /// <summary>
        /// Zaznamená výsledek použití modelu
        /// </summary>
        public void RecordModelResult(string modelId, TaskType taskType, bool success, double confidenceLevel)
        {
            string key = $"{modelId}_{taskType}";
            if (!_modelStats.ContainsKey(key))
            {
                _modelStats[key] = new ModelStats
                {
                    ModelId = modelId,
                    TaskType = taskType,
                    TotalAttempts = 0,
                    SuccessfulAttempts = 0,
                    SuccessRate = 0.5,
                    LastUpdateTime = DateTime.Now
                };
            }

            var stats = _modelStats[key];
            stats.TotalAttempts++;

            if (success)
            {
                stats.SuccessfulAttempts++;
            }

            // Aktualizace úspěšnosti s vážením podle confidence
            double factor = _learningRate * confidenceLevel;
            if (success)
            {
                stats.SuccessRate = stats.SuccessRate * (1 - factor) + factor;
            }
            else
            {
                stats.SuccessRate = stats.SuccessRate * (1 - factor);
            }

            stats.LastUpdateTime = DateTime.Now;

            LogMessage($"Zaznamenán výsledek pro model {modelId}: {(success ? "úspěch" : "neúspěch")}, míra úspěšnosti: {stats.SuccessRate:P0}");

            // Publikace události o nahrání výsledku
            _eventBus.Publish(new ResultRecordedEvent(modelId, success, confidenceLevel));
        }

        /// <summary>
        /// Zaznamená výsledek použití promptu
        /// </summary>
        public void RecordPromptResult(string promptType, bool success, double confidenceLevel)
        {
            // Podobná implementace jako pro modely...
            LogMessage($"Zaznamenán výsledek pro prompt {promptType}: {(success ? "úspěch" : "neúspěch")}");
        }

        /// <summary>
        /// Zaznamená výsledek použití strategie
        /// </summary>
        public void RecordStrategyResult(string strategy, bool success, double confidenceLevel)
        {
            // Podobná implementace jako pro modely...
            LogMessage($"Zaznamenán výsledek pro strategii {strategy}: {(success ? "úspěch" : "neúspěch")}");
        }

        /// <summary>
        /// Získá statistiky všech modelů
        /// </summary>
        public List<ModelStats> GetModelStats()
        {
            return _modelStats.Values.ToList();
        }

        /// <summary>
        /// Získá statistiky všech promptů
        /// </summary>
        public List<PromptStats> GetPromptStats()
        {
            return _promptStats.Values.ToList();
        }

        /// <summary>
        /// Získá statistiky všech strategií
        /// </summary>
        public List<StrategyStats> GetStrategyStats()
        {
            return _strategyStats.Values.ToList();
        }

        #region Pomocné metody

        /// <summary>
        /// Rozhoduje, zda by měl systém experimentovat
        /// </summary>
        private bool ShouldExplore()
        {
            // Jednoduché rozhodování na základě náhodného čísla
            return new Random().NextDouble() < _explorationRate;
        }

        /// <summary>
        /// Vybere experimentální model pro testování
        /// </summary>
        private ModelSelectionResult SelectExperimentalModel(TaskType taskType)
        {
            var eligibleModels = _modelStats.Values
                .Where(s => s.TaskType == taskType)
                .ToList();

            if (eligibleModels.Count == 0)
            {
                return new ModelSelectionResult
                {
                    ModelId = GetDefaultModelForTask(taskType),
                    TaskType = taskType,
                    Confidence = 0.5,
                    IsExperimental = true
                };
            }

            var selectedModel = eligibleModels[new Random().Next(eligibleModels.Count)];

            LogMessage($"Experimentální výběr modelu: {selectedModel.ModelId} pro úkol {taskType}");

            return new ModelSelectionResult
            {
                ModelId = selectedModel.ModelId,
                TaskType = taskType,
                Confidence = selectedModel.SuccessRate,
                IsExperimental = true
            };
        }

        /// <summary>
        /// Vybere nejlepší známý model pro úkol
        /// </summary>
        private ModelSelectionResult SelectBestModel(TaskType taskType, ModelSelectionContext context)
        {
            var eligibleModels = _modelStats.Values
                .Where(s => s.TaskType == taskType)
                .ToList();

            if (eligibleModels.Count == 0)
            {
                return new ModelSelectionResult
                {
                    ModelId = GetDefaultModelForTask(taskType),
                    TaskType = taskType,
                    Confidence = 0.5,
                    IsExperimental = false
                };
            }

            // Pro složité úkoly preferujeme větší modely
            if (context.IsComplexTask)
            {
                var biggerModels = eligibleModels.Where(m => m.ModelId.Contains("13b")).ToList();
                if (biggerModels.Count > 0)
                {
                    eligibleModels = biggerModels;
                }
            }

            // Pro opravy preferujeme menší, specializované modely
            if (taskType == TaskType.ErrorRepair)
            {
                var repairModels = eligibleModels.Where(m => m.ModelId.Contains("7b")).ToList();
                if (repairModels.Count > 0)
                {
                    eligibleModels = repairModels;
                }
            }

            // Vybereme model s nejvyšší úspěšností
            var bestModel = eligibleModels.OrderByDescending(m => m.SuccessRate).First();

            LogMessage($"Vybrán nejlepší model: {bestModel.ModelId} pro úkol {taskType}, úspěšnost: {bestModel.SuccessRate:P0}");

            return new ModelSelectionResult
            {
                ModelId = bestModel.ModelId,
                TaskType = taskType,
                Confidence = bestModel.SuccessRate,
                IsExperimental = false
            };
        }

        /// <summary>
        /// Vybere experimentální prompt pro testování
        /// </summary>
        private string SelectExperimentalPrompt(PromptType promptType)
        {
            // Podobná implementace jako pro modely...
            return promptType.ToString() + "_Standard";
        }

        /// <summary>
        /// Vybere nejlepší prompt pro daný typ
        /// </summary>
        private string SelectBestPrompt(PromptType promptType, PromptSelectionContext context)
        {
            // Podobná implementace jako pro modely...
            return promptType.ToString() + "_Standard";
        }

        /// <summary>
        /// Vybere experimentální strategii
        /// </summary>
        private string SelectExperimentalStrategy(string situation)
        {
            // Podobná implementace jako pro modely...
            return situation + "_Standard";
        }

        /// <summary>
        /// Vybere nejlepší strategii pro danou situaci
        /// </summary>
        private string SelectBestStrategy(string situation, StrategySelectionContext context)
        {
            // Podobná implementace jako pro modely...
            return situation + "_Standard";
        }

        /// <summary>
        /// Získá výchozí model pro daný typ úkolu
        /// </summary>
        private string GetDefaultModelForTask(TaskType taskType)
        {
            switch (taskType)
            {
                case TaskType.CodeGeneration:
                    return "codellama:13b";
                case TaskType.ErrorRepair:
                    return "codellama:7b";
                case TaskType.Testing:
                    return "mistral:7b";
                default:
                    return "codellama:13b";
            }
        }

        /// <summary>
        /// Registruje výchozí modely při inicializaci
        /// </summary>
        private void RegisterDefaultModels()
        {
            // Registrace modelů pro generování kódu
            _modelStats["codellama:13b_CodeGeneration"] = new ModelStats
            {
                ModelId = "codellama:13b",
                TaskType = TaskType.CodeGeneration,
                TotalAttempts = 10,
                SuccessfulAttempts = 8,
                SuccessRate = 0.8,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };

            _modelStats["codellama:7b_CodeGeneration"] = new ModelStats
            {
                ModelId = "codellama:7b",
                TaskType = TaskType.CodeGeneration,
                TotalAttempts = 10,
                SuccessfulAttempts = 7,
                SuccessRate = 0.7,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };

            _modelStats["mistral:7b_CodeGeneration"] = new ModelStats
            {
                ModelId = "mistral:7b",
                TaskType = TaskType.CodeGeneration,
                TotalAttempts = 10,
                SuccessfulAttempts = 6,
                SuccessRate = 0.6,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };

            // Registrace modelů pro opravy chyb
            _modelStats["codellama:13b_ErrorRepair"] = new ModelStats
            {
                ModelId = "codellama:13b",
                TaskType = TaskType.ErrorRepair,
                TotalAttempts = 10,
                SuccessfulAttempts = 7,
                SuccessRate = 0.7,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };

            _modelStats["codellama:7b_ErrorRepair"] = new ModelStats
            {
                ModelId = "codellama:7b",
                TaskType = TaskType.ErrorRepair,
                TotalAttempts = 10,
                SuccessfulAttempts = 8,
                SuccessRate = 0.8,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };

            _modelStats["mistral:7b_ErrorRepair"] = new ModelStats
            {
                ModelId = "mistral:7b",
                TaskType = TaskType.ErrorRepair,
                TotalAttempts = 10,
                SuccessfulAttempts = 7,
                SuccessRate = 0.7,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };

            // Registrace modelů pro testování
            _modelStats["codellama:13b_Testing"] = new ModelStats
            {
                ModelId = "codellama:13b",
                TaskType = TaskType.Testing,
                TotalAttempts = 10,
                SuccessfulAttempts = 6,
                SuccessRate = 0.6,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };

            _modelStats["codellama:7b_Testing"] = new ModelStats
            {
                ModelId = "codellama:7b",
                TaskType = TaskType.Testing,
                TotalAttempts = 10,
                SuccessfulAttempts = 7,
                SuccessRate = 0.7,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };

            _modelStats["mistral:7b_Testing"] = new ModelStats
            {
                ModelId = "mistral:7b",
                TaskType = TaskType.Testing,
                TotalAttempts = 10,
                SuccessfulAttempts = 9,
                SuccessRate = 0.9,
                LastUpdateTime = DateTime.Now.AddDays(-1)
            };
        }

        /// <summary>
        /// Registruje výchozí prompty při inicializaci
        /// </summary>
        private void RegisterDefaultPrompts()
        {
            // Tady by byla podobná inicializace pro prompty
        }

        /// <summary>
        /// Registruje výchozí strategie při inicializaci
        /// </summary>
        private void RegisterDefaultStrategies()
        {
            // Tady by byla podobná inicializace pro strategie
        }

        /// <summary>
        /// Handler události o dokončení kompilace
        /// </summary>
        private void OnCompilationCompleted(CompilationCompletedEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.ModelId))
            {
                RecordModelResult(evt.ModelId, TaskType.CodeGeneration, evt.Success, evt.Success ? 1.0 : 0.0);
            }
        }

        /// <summary>
        /// Handler události o dokončení analýzy formuláře
        /// </summary>
        private void OnFormAnalysisCompleted(FormAnalysisCompletedEvent evt)
        {
            // Zde bychom mohli něco udělat po analýze formuláře
        }

        /// <summary>
        /// Loguje zprávu přes event bus
        /// </summary>
        private void LogMessage(string message, MessageSeverity severity = MessageSeverity.Info)
        {
            var systemMessage = new SystemMessage
            {
                Message = message,
                Severity = severity,
                Timestamp = DateTime.Now,
                Source = "AdaptiveLearningModel"
            };

            _eventBus.Publish(new SystemMessageEvent(systemMessage));
        }

        #endregion
    }

    /// <summary>
    /// Statistiky modelu
    /// </summary>
    public class ModelStats
    {
        public string ModelId { get; set; }
        public TaskType TaskType { get; set; }
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public double SuccessRate { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }

    /// <summary>
    /// Statistiky promptu
    /// </summary>
    public class PromptStats
    {
        public string PromptId { get; set; }
        public string PromptType { get; set; }
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public double SuccessRate { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }

    /// <summary>
    /// Statistiky strategie
    /// </summary>
    public class StrategyStats
    {
        public string StrategyId { get; set; }
        public string Situation { get; set; }
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public double SuccessRate { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }

    /// <summary>
    /// Kontext pro výběr modelu
    /// </summary>
    public class ModelSelectionContext
    {
        public bool IsComplexTask { get; set; }
        public int PreviousAttempts { get; set; }
        public string CurrentStage { get; set; }
        public bool TimeConstraint { get; set; }
    }

    /// <summary>
    /// Kontext pro výběr promptu
    /// </summary>
    public class PromptSelectionContext
    {
        public bool IsSimpleTask { get; set; }
        public int RepairAttempts { get; set; }
        public string CurrentStage { get; set; }
        public bool DetailedOutput { get; set; }
    }

    /// <summary>
    /// Kontext pro výběr strategie
    /// </summary>
    public class StrategySelectionContext
    {
        public int ErrorCount { get; set; }
        public bool TimeConstraint { get; set; }
        public string CurrentStage { get; set; }
        public bool IsExperimental { get; set; }
    }

    /// <summary>
    /// Událost publikovaná při výběru modelu
    /// </summary>
    public class ModelSelectedEvent
    {
        public ModelSelectionResult Selection { get; }

        public ModelSelectedEvent(ModelSelectionResult selection)
        {
            Selection = selection;
        }
    }
}