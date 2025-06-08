using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoKiwi.Minimal;
using AutoKiwi.Translation;

namespace AutoKiwi.Orchestration
{
    /// <summary>
    /// Adaptivní učící se modul - implementace podle PDF dokumentu
    /// Umožňuje systému učit se z vlastních akcí a zlepšovat se
    /// </summary>
    public class AdaptiveSelector<T>
    {
        private readonly IEventBus _eventBus;
        private readonly Dictionary<string, SuccessRate> _successRates;
        private readonly Random _random = new Random();

        // Konfigurace adaptace
        private readonly double _explorationRate = 0.1; // Míra průzkumu nových možností (10%)
        private readonly double _learningRate = 0.2; // Rychlost učení (20%)
        private readonly double _decayRate = 0.95; // Rychlost zapomínání (5% každou iteraci)

        public AdaptiveSelector(IEventBus eventBus)
        {
            _eventBus = eventBus;
            _successRates = new Dictionary<string, SuccessRate>();
        }

        /// <summary>
        /// Vybere nejlepší strategii/model/akci pro daný úkol
        /// Kombinuje historickou úspěšnost s kontextovou podobností
        /// </summary>
        public async Task<T> SelectAsync(string task, Func<string, double> contextSimilarity)
        {
            LogMessage($"Výběr nejlepší strategie pro úkol: {task}");

            // Získání všech možností pro daný úkol
            var options = _successRates
                .Where(sr => sr.Key.StartsWith(task + ":"))
                .ToList();

            // Pokud nemáme žádné možnosti, vrátíme výchozí hodnotu
            if (options.Count == 0)
            {
                LogMessage("Žádné možnosti k výběru, vracím výchozí hodnotu");
                return default(T);
            }

            // Rozhodnutí mezi průzkumem a využitím (exploration vs exploitation)
            if (_random.NextDouble() < _explorationRate)
            {
                // Průzkum - náhodný výběr
                var randomOption = options[_random.Next(options.Count)];
                string optionId = randomOption.Key.Substring(task.Length + 1);

                LogMessage($"Průzkumný výběr: {optionId}");

                // Publikování události o výběru
                _eventBus.Publish(new SelectionMadeEvent(task, optionId, "exploration", 0.0));

                return (T)Convert.ChangeType(optionId, typeof(T));
            }

            // Využití - výběr nejlepší možnosti podle úspěšnosti a kontextové podobnosti
            var scoredOptions = new List<(string OptionId, double Score)>();

            foreach (var option in options)
            {
                string optionId = option.Key.Substring(task.Length + 1);

                // Kombinace historické úspěšnosti a kontextové podobnosti
                double successScore = option.Value.SuccessRate;
                double contextScore = contextSimilarity(optionId);

                // Vážený průměr (70% úspěšnost, 30% kontextová podobnost)
                double score = (successScore * 0.7) + (contextScore * 0.3);

                scoredOptions.Add((optionId, score));
            }

            // Seřazení podle skóre
            scoredOptions.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Výběr nejlepší možnosti
            string selectedOption = scoredOptions[0].OptionId;
            double selectedScore = scoredOptions[0].Score;

            LogMessage($"Výběr na základě úspěšnosti: {selectedOption} (skóre: {selectedScore:F2})");

            // Publikování události o výběru
            _eventBus.Publish(new SelectionMadeEvent(task, selectedOption, "exploitation", selectedScore));

            return (T)Convert.ChangeType(selectedOption, typeof(T));
        }

        /// <summary>
        /// Zaznamenává výsledek použití vybrané strategie/modelu/akce
        /// </summary>
        public void RecordResult(string selected, bool success, double confidenceLevel)
        {
            // Pokud nemáme vybranou možnost, nemůžeme zaznamenat výsledek
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            LogMessage($"Zaznamenávám výsledek pro {selected}: {(success ? "úspěch" : "neúspěch")} (důvěra: {confidenceLevel:F2})");

            // Aktualizace úspěšnosti pro všechny relevantní úkoly
            foreach (var key in _successRates.Keys.ToList())
            {
                // Aktualizace konkrétní strategie
                if (key.EndsWith(":" + selected))
                {
                    UpdateSuccessRate(key, success, confidenceLevel);
                }
                else
                {
                    // Pro ostatní strategie aplikujeme decay
                    ApplyDecay(key);
                }
            }

            // Publikování události o výsledku
            _eventBus.Publish(new ResultRecordedEvent(selected, success, confidenceLevel));
        }

        /// <summary>
        /// Aktualizuje úspěšnost pro danou strategii
        /// </summary>
        private void UpdateSuccessRate(string key, bool success, double confidenceLevel)
        {
            if (!_successRates.ContainsKey(key))
            {
                _successRates[key] = new SuccessRate();
            }

            var rate = _successRates[key];

            // Aplikace učícího faktoru - vážená aktualizace
            double learningFactor = _learningRate * confidenceLevel;

            if (success)
            {
                // Zvýšení úspěšnosti
                rate.SuccessRate = rate.SuccessRate * (1 - learningFactor) + 1.0 * learningFactor;
            }
            else
            {
                // Snížení úspěšnosti
                rate.SuccessRate = rate.SuccessRate * (1 - learningFactor) + 0.0 * learningFactor;
            }

            // Aktualizace počtu pokusů
            rate.TotalAttempts++;
            if (success)
            {
                rate.SuccessfulAttempts++;
            }

            // Aktualizace poslední úspěšnosti
            rate.LastSuccess = success;
            rate.LastConfidence = confidenceLevel;
            rate.LastUpdateTime = DateTime.Now;
        }

        /// <summary>
        /// Aplikuje decay (zapomínání) pro danou strategii
        /// </summary>
        private void ApplyDecay(string key)
        {
            if (!_successRates.ContainsKey(key))
            {
                return;
            }

            var rate = _successRates[key];

            // Aplikace decay faktoru - postupné zapomínání starých výsledků
            rate.SuccessRate = rate.SuccessRate * _decayRate;
        }

        /// <summary>
        /// Vrací statistiky úspěšnosti pro zadaný úkol
        /// </summary>
        public IEnumerable<SuccessStatistics> GetStatistics(string task)
        {
            var statistics = new List<SuccessStatistics>();

            foreach (var entry in _successRates)
            {
                if (entry.Key.StartsWith(task + ":"))
                {
                    string optionId = entry.Key.Substring(task.Length + 1);
                    var rate = entry.Value;

                    statistics.Add(new SuccessStatistics
                    {
                        TaskName = task,
                        OptionId = optionId,
                        SuccessRate = rate.SuccessRate,
                        TotalAttempts = rate.TotalAttempts,
                        SuccessfulAttempts = rate.SuccessfulAttempts,
                        LastUpdateTime = rate.LastUpdateTime
                    });
                }
            }

            return statistics;
        }

        /// <summary>
        /// Registruje novou možnost pro daný úkol
        /// </summary>
        public void RegisterOption(string task, T option, double initialSuccessRate = 0.5)
        {
            string optionId = option.ToString();
            string key = $"{task}:{optionId}";

            if (!_successRates.ContainsKey(key))
            {
                _successRates[key] = new SuccessRate
                {
                    SuccessRate = initialSuccessRate,
                    TotalAttempts = 0,
                    SuccessfulAttempts = 0,
                    LastUpdateTime = DateTime.Now
                };

                LogMessage($"Registrována nová možnost pro úkol {task}: {optionId} (výchozí úspěšnost: {initialSuccessRate:F2})");
            }
        }

        /// <summary>
        /// Loguje zprávu
        /// </summary>
        private void LogMessage(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [AdaptiveSelector] {message}");
        }
    }

    /// <summary>
    /// Model adaptivního učení - implementuje rozhraní podle PDF dokumentu
    /// Poskytuje adaptivní výběr modelů, strategií a akcí
    /// </summary>
    public class AdaptiveLearningModel
    {
        private readonly IEventBus _eventBus;
        private readonly AdaptiveSelector<string> _modelSelector;
        private readonly AdaptiveSelector<string> _promptSelector;
        private readonly AdaptiveSelector<string> _strategySelector;
        private readonly ContextManager _contextManager;

        public AdaptiveLearningModel(
            IEventBus eventBus,
            ContextManager contextManager)
        {
            _eventBus = eventBus;
            _contextManager = contextManager;

            // Inicializace selektorů
            _modelSelector = new AdaptiveSelector<string>(_eventBus);
            _promptSelector = new AdaptiveSelector<string>(_eventBus);
            _strategySelector = new AdaptiveSelector<string>(_eventBus);

            // Registrace modelů
            RegisterDefaultModels();
            RegisterDefaultPrompts();
            RegisterDefaultStrategies();

            // Přihlášení k událostem
            _eventBus.Subscribe<CompilationCompletedEvent>(OnCompilationCompleted);
            _eventBus.Subscribe<FormAnalysisCompletedEvent>(OnFormAnalysisCompleted);
            _eventBus.Subscribe<ModelSelectionCompletedEvent>(OnModelSelectionCompleted);
            _eventBus.Subscribe<WorkflowStateChangedEvent>(OnWorkflowStateChanged);
        }

        /// <summary>
        /// Vybere optimální model pro daný typ úkolu
        /// </summary>
        public async Task<ModelSelectionResult> SelectModelAsync(
            TaskType taskType,
            ModelSelectionContext context)
        {
            LogMessage($"Výběr modelu pro úkol: {taskType}");

            // Funkce pro výpočet kontextové podobnosti
            Func<string, double> similarityCalculator = (modelId) => {
                // Implementace vhodné heuristiky podle typu úkolu
                if (taskType == TaskType.CodeGeneration && context.IsComplexTask && modelId.Contains("13b"))
                {
                    return 0.8; // Větší modely jsou lepší pro složité úkoly
                }

                if (taskType == TaskType.ErrorRepair && modelId.Contains("7b"))
                {
                    return 0.7; // Střední modely jsou dobré pro opravy
                }

                if (taskType == TaskType.Testing && modelId.Contains("mistral"))
                {
                    return 0.9; // Mistral je vhodný pro testování
                }

                return 0.5; // Výchozí podobnost
            };

            // Výběr modelu
            string model = await _modelSelector.SelectAsync(taskType.ToString(), similarityCalculator);

            // Pokud nebyl vybrán žádný model, použijeme výchozí
            if (string.IsNullOrEmpty(model))
            {
                model = GetDefaultModelForTask(taskType);
            }

            // Vytvoření výsledku
            var result = new ModelSelectionResult
            {
                ModelId = model,
                TaskType = taskType,
                Confidence = GetConfidenceForModel(model, taskType),
                IsExperimental = false // TODO: Implementovat experimentální výběr
            };

            LogMessage($"Vybrán model: {result.ModelId} (důvěra: {result.Confidence:F2})");

            // Publikování události o výběru modelu
            _eventBus.Publish(new ModelSelectedEvent(result));

            return result;
        }

        /// <summary>
        /// Vybere optimální typ promptu pro daný úkol
        /// </summary>
        public async Task<string> SelectPromptTypeAsync(
            PromptType baseType,
            PromptSelectionContext context)
        {
            LogMessage($"Výběr typu promptu pro: {baseType}");

            // Funkce pro výpočet kontextové podobnosti
            Func<string, double> similarityCalculator = (promptType) => {
                // Implementace vhodné heuristiky podle kontextu
                if (context.RepairAttempts > 1 && promptType.Contains("Detail"))
                {
                    return 0.8; // Detailnější prompty pro opakované opravy
                }

                if (context.IsSimpleTask && promptType.Contains("Simple"))
                {
                    return 0.7; // Jednodušší prompty pro jednoduché úkoly
                }

                if (promptType.Contains(baseType.ToString()))
                {
                    return 0.6; // Preference promptů pro daný typ
                }

                return 0.5; // Výchozí podobnost
            };

            // Výběr typu promptu
            string promptType = await _promptSelector.SelectAsync(baseType.ToString(), similarityCalculator);

            // Pokud nebyl vybrán žádný typ, použijeme výchozí
            if (string.IsNullOrEmpty(promptType))
            {
                promptType = baseType.ToString();
            }

            LogMessage($"Vybrán typ promptu: {promptType}");

            return promptType;
        }

        /// <summary>
        /// Vybere optimální strategii pro danou situaci
        /// </summary>
        public async Task<string> SelectStrategyAsync(
            string situation,
            StrategySelectionContext context)
        {
            LogMessage($"Výběr strategie pro situaci: {situation}");

            // Funkce pro výpočet kontextové podobnosti
            Func<string, double> similarityCalculator = (strategy) => {
                // Implementace vhodné heuristiky podle kontextu
                if (context.ErrorCount > 2 && strategy.Contains("Conservative"))
                {
                    return 0.8; // Konzervativnější strategie po mnoha chybách
                }

                if (context.TimeConstraint && strategy.Contains("Fast"))
                {
                    return 0.9; // Rychlejší strategie při časovém omezení
                }

                if (strategy.Contains(situation))
                {
                    return 0.7; // Preference strategií pro danou situaci
                }

                return 0.5; // Výchozí podobnost
            };

            // Výběr strategie
            string strategy = await _strategySelector.SelectAsync(situation, similarityCalculator);

            // Pokud nebyla vybrána žádná strategie, použijeme výchozí
            if (string.IsNullOrEmpty(strategy))
            {
                strategy = "Default";
            }

            LogMessage($"Vybrána strategie: {strategy}");

            return strategy;
        }

        /// <summary>
        /// Zaznamenává výsledek použití modelu
        /// </summary>
        public void RecordModelResult(
            string modelId,
            TaskType taskType,
            bool success,
            double confidenceLevel)
        {
            LogMessage($"Zaznamenávám výsledek modelu {modelId} pro úkol {taskType}: {(success ? "úspěch" : "neúspěch")}");

            _modelSelector.RecordResult(modelId, success, confidenceLevel);
        }

        /// <summary>
        /// Zaznamenává výsledek použití typu promptu
        /// </summary>
        public void RecordPromptResult(
            string promptType,
            bool success,
            double confidenceLevel)
        {
            LogMessage($"Zaznamenávám výsledek typu promptu {promptType}: {(success ? "úspěch" : "neúspěch")}");

            _promptSelector.RecordResult(promptType, success, confidenceLevel);
        }

        /// <summary>
        /// Zaznamenává výsledek použití strategie
        /// </summary>
        public void RecordStrategyResult(
            string strategy,
            bool success,
            double confidenceLevel)
        {
            LogMessage($"Zaznamenávám výsledek strategie {strategy}: {(success ? "úspěch" : "neúspěch")}");

            _strategySelector.RecordResult(strategy, success, confidenceLevel);
        }

        /// <summary>
        /// Registruje výchozí modely
        /// </summary>
        private void RegisterDefaultModels()
        {
            // Registrace modelů pro generování kódu
            _modelSelector.RegisterOption(TaskType.CodeGeneration.ToString(), "codellama:13b", 0.8);
            _modelSelector.RegisterOption(TaskType.CodeGeneration.ToString(), "codellama:7b", 0.7);
            _modelSelector.RegisterOption(TaskType.CodeGeneration.ToString(), "mistral:7b", 0.6);

            // Registrace modelů pro opravy chyb
            _modelSelector.RegisterOption(TaskType.ErrorRepair.ToString(), "codellama:7b", 0.8);
            _modelSelector.RegisterOption(TaskType.ErrorRepair.ToString(), "codellama:13b", 0.7);
            _modelSelector.RegisterOption(TaskType.ErrorRepair.ToString(), "mistral:7b", 0.6);

            // Registrace modelů pro testování
            _modelSelector.RegisterOption(TaskType.Testing.ToString(), "mistral:7b", 0.8);
            _modelSelector.RegisterOption(TaskType.Testing.ToString(), "codellama:7b", 0.7);
            _modelSelector.RegisterOption(TaskType.Testing.ToString(), "codellama:13b", 0.6);

            // Registrace modelů pro plánování
            _modelSelector.RegisterOption(TaskType.Planning.ToString(), "mistral:7b", 0.8);
            _modelSelector.RegisterOption(TaskType.Planning.ToString(), "codellama:13b", 0.7);
        }

        /// <summary>
        /// Registruje výchozí typy promptů
        /// </summary>
        private void RegisterDefaultPrompts()
        {
            // Registrace typů promptů pro generování kódu
            _promptSelector.RegisterOption(PromptType.CodeGeneration.ToString(), "Standard", 0.7);
            _promptSelector.RegisterOption(PromptType.CodeGeneration.ToString(), "Detailed", 0.6);
            _promptSelector.RegisterOption(PromptType.CodeGeneration.ToString(), "Simple", 0.5);

            // Registrace typů promptů pro opravy chyb
            _promptSelector.RegisterOption(PromptType.ErrorRepair.ToString(), "Standard", 0.7);
            _promptSelector.RegisterOption(PromptType.ErrorRepair.ToString(), "Focused", 0.6);
            _promptSelector.RegisterOption(PromptType.ErrorRepair.ToString(), "Detailed", 0.5);

            // Registrace typů promptů pro testování
            _promptSelector.RegisterOption(PromptType.TestApplication.ToString(), "Standard", 0.7);
            _promptSelector.RegisterOption(PromptType.TestApplication.ToString(), "Comprehensive", 0.6);
            _promptSelector.RegisterOption(PromptType.TestApplication.ToString(), "Focused", 0.5);
        }

        /// <summary>
        /// Registruje výchozí strategie
        /// </summary>
        private void RegisterDefaultStrategies()
        {
            // Registrace strategií pro generování
            _strategySelector.RegisterOption("Generation", "Standard", 0.7);
            _strategySelector.RegisterOption("Generation", "Incremental", 0.6);
            _strategySelector.RegisterOption("Generation", "Component", 0.5);

            // Registrace strategií pro opravy
            _strategySelector.RegisterOption("Repair", "Standard", 0.7);
            _strategySelector.RegisterOption("Repair", "Conservative", 0.6);
            _strategySelector.RegisterOption("Repair", "Aggressive", 0.5);

            // Registrace strategií pro testování
            _strategySelector.RegisterOption("Testing", "Standard", 0.7);
            _strategySelector.RegisterOption("Testing", "Comprehensive", 0.6);
            _strategySelector.RegisterOption("Testing", "Fast", 0.5);
        }

        /// <summary>
        /// Vrací výchozí model pro daný typ úkolu
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
                case TaskType.Planning:
                    return "mistral:7b";
                default:
                    return "codellama:13b";
            }
        }

        /// <summary>
        /// Vrací důvěru pro daný model a typ úkolu
        /// </summary>
        private double GetConfidenceForModel(string modelId, TaskType taskType)
        {
            // Získání statistik pro daný model a úkol
            var statistics = _modelSelector.GetStatistics(taskType.ToString())
                .FirstOrDefault(s => s.OptionId == modelId);

            if (statistics != null)
            {
                return statistics.SuccessRate;
            }

            // Výchozí důvěra
            return 0.5;
        }

        /// <summary>
        /// Event handler pro událost CompilationCompletedEvent
        /// </summary>
        private void OnCompilationCompleted(CompilationCompletedEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.ModelId))
            {
                // Zaznamenání výsledku modelu
                RecordModelResult(
                    evt.ModelId,
                    TaskType.CodeGeneration,
                    evt.Success,
                    evt.Success ? 1.0 : 0.0);

                // Zaznamenání výsledku strategie
                RecordStrategyResult(
                    "Generation",
                    evt.Success,
                    evt.Success ? 1.0 : 0.0);
            }
        }

        /// <summary>
        /// Event handler pro událost FormAnalysisCompletedEvent
        /// </summary>
        private void OnFormAnalysisCompleted(FormAnalysisCompletedEvent evt)
        {
            // Zatím nepotřebujeme zpracovávat tuto událost
        }

        /// <summary>
        /// Event handler pro událost ModelSelectionCompletedEvent
        /// </summary>
        private void OnModelSelectionCompleted(ModelSelectionCompletedEvent evt)
        {
            // Zatím nepotřebujeme zpracovávat tuto událost
        }

        /// <summary>
        /// Event handler pro událost WorkflowStateChangedEvent
        /// </summary>
        private void OnWorkflowStateChanged(WorkflowStateChangedEvent evt)
        {
            // Zatím nepotřebujeme zpracovávat tuto událost
        }

        /// <summary>
        /// Loguje zprávu
        /// </summary>
        private void LogMessage(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [AdaptiveLearning] {message}");

            // Publikování události
            _eventBus.Publish(new SystemMessageEvent(new SystemMessage
            {
                Message = message,
                Severity = MessageSeverity.Info,
                Timestamp = DateTime.Now,
                Source = "AdaptiveLearning"
            }));
        }
    }

    #region Podpůrné třídy

    /// <summary>
    /// Interní třída pro sledování úspěšnosti
    /// </summary>
    public class SuccessRate
    {
        public double Rate { get; set; } = 0.5; // Výchozí úspěšnost 50%
        public int TotalAttempts { get; set; } = 0;
        public int SuccessfulAttempts { get; set; } = 0;
        public bool LastSuccess { get; set; } = false;
        public double LastConfidence { get; set; } = 0.0;
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Statistika úspěšnosti pro výstup
    /// </summary>
    public class SuccessStatistics
    {
        public string TaskName { get; set; }
        public string OptionId { get; set; }
        public double SuccessRate { get; set; }
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
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
    /// Kontext pro výběr typu promptu
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
    /// Událost publikovaná při výběru strategie/modelu/akce
    /// </summary>
    public class SelectionMadeEvent
    {
        public string TaskName { get; }
        public string SelectedOption { get; }
        public string SelectionType { get; }
        public double Score { get; }

        public SelectionMadeEvent(string taskName, string selectedOption, string selectionType, double score)
        {
            TaskName = taskName;
            SelectedOption = selectedOption;
            SelectionType = selectionType;
            Score = score;
        }
    }

    /// <summary>
    /// Událost publikovaná při zaznamenání výsledku
    /// </summary>
    public class ResultRecordedEvent
    {
        public string SelectedOption { get; }
        public bool Success { get; }
        public double ConfidenceLevel { get; }

        public ResultRecordedEvent(string selectedOption, bool success, double confidenceLevel)
        {
            SelectedOption = selectedOption;
            Success = success;
            ConfidenceLevel = confidenceLevel;
        }
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

    #endregion
}