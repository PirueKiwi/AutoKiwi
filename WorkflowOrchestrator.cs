// File: WorkflowOrchestrator.cs
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoKiwi.Control;
using AutoKiwi.Generation;
using AutoKiwi.Memory;
using AutoKiwi.Translation;

namespace AutoKiwi
{
    /// <summary>
    /// Hlavní orchestrátor workflow - koordinuje celý proces generování aplikace
    /// </summary>
    public class WorkflowOrchestrator
    {
        private readonly Dirigent _dirigent;
        private readonly IEventBus _eventBus;
        private readonly MonitoringDashboard _dashboard;

        public WorkflowOrchestrator(
            Dirigent dirigent,
            IEventBus eventBus,
            MonitoringDashboard dashboard = null)
        {
            _dirigent = dirigent ?? throw new ArgumentNullException(nameof(dirigent));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _dashboard = dashboard;

            // Propojení s dashboardem, pokud existuje
            _dashboard?.SetDirigent(_dirigent);

            // Přihlášení k událostem
            _eventBus.Subscribe<WorkflowStateChangedEvent>(OnWorkflowStateChanged);
        }

        /// <summary>
        /// Spustí celý vývojový cyklus pro zadaný popis aplikace
        /// </summary>
        public async Task RunDevelopmentCycleAsync(string applicationDescription)
        {
            LogMessage("Spouštím vývojový cyklus");

            try
            {
                // Fáze 1: Vygenerování kódu
                await _dirigent.StartDevelopmentSequenceAsync(applicationDescription);

                // Fáze 2: Testovací a učící smyčka
                bool continueCycle = true;
                int iteration = 0;
                const int maxIterations = 10;

                while (continueCycle && iteration < maxIterations)
                {
                    iteration++;
                    LogMessage($"Iterace ovládání a učení #{iteration}");

                    await _dirigent.ControlAndLearnAsync();

                    // Podmínka pro ukončení - např. pokud jsme dosáhli cíle nebo maximálního počtu iterací
                    continueCycle = ShouldContinueIteration(iteration);
                }

                LogMessage("Vývojový cyklus dokončen");
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba ve workflow: {ex.Message}", MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Rozhoduje, zda pokračovat v další iteraci
        /// </summary>
        private bool ShouldContinueIteration(int currentIteration)
        {
            // Tady by byla složitější logika, například zda jsme dosáhli požadované kvality
            // Pro jednoduchost omezíme jen na počet iterací
            return currentIteration < 3 && _dirigent.CurrentOperation != null;
        }

        /// <summary>
        /// Handler pro změnu stavu workflow
        /// </summary>
        private void OnWorkflowStateChanged(WorkflowStateChangedEvent evt)
        {
            LogMessage($"Změna stavu workflow: {evt.State.PreviousStage} -> {evt.State.CurrentStage}");

            // Pokud workflow skončilo, můžeme reagovat
            if (evt.State.CurrentStage == WorkflowStage.Completed ||
                evt.State.CurrentStage == WorkflowStage.Error)
            {
                LogMessage(evt.State.CurrentStage == WorkflowStage.Completed
                    ? "Workflow úspěšně dokončeno"
                    : "Workflow skončilo chybou",
                    evt.State.CurrentStage == WorkflowStage.Completed
                        ? MessageSeverity.Info
                        : MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Logování zprávy přes event bus
        /// </summary>
        private void LogMessage(string message, MessageSeverity severity = MessageSeverity.Info)
        {
            var systemMessage = new SystemMessage
            {
                Message = message,
                Severity = severity,
                Timestamp = DateTime.Now,
                Source = "WorkflowOrchestrator"
            };

            _eventBus.Publish(new SystemMessageEvent(systemMessage));
        }
    }

    /// <summary>
    /// Dirigent - jádro systému, které řídí jednotlivé kroky procesu
    /// </summary>
    public class Dirigent
    {
        private readonly IEventBus _eventBus;
        private readonly ICodeGenerator _codeGenerator;
        private readonly ICompilationEngine _compilationEngine;
        private readonly IFormAnalyzer _formAnalyzer;
        private readonly IApplicationMemory _appMemory;
        private readonly Win2TextController _controller;

        private WorkflowState _currentState;
        private DirigentOperation _currentOperation;
        private Assembly _compiledAssembly;
        private Form _runningForm;
        private string _generatedCode;

        // Maximální počet pokusů o opravu
        private const int MaxRepairAttempts = 5;

        public DirigentOperation CurrentOperation => _currentOperation;

        public Dirigent(
            IEventBus eventBus,
            ICodeGenerator codeGenerator,
            ICompilationEngine compilationEngine,
            IFormAnalyzer formAnalyzer,
            IApplicationMemory appMemory,
            Win2TextController controller)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _codeGenerator = codeGenerator ?? throw new ArgumentNullException(nameof(codeGenerator));
            _compilationEngine = compilationEngine ?? throw new ArgumentNullException(nameof(compilationEngine));
            _formAnalyzer = formAnalyzer ?? throw new ArgumentNullException(nameof(formAnalyzer));
            _appMemory = appMemory ?? throw new ArgumentNullException(nameof(appMemory));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));

            _currentState = new WorkflowState
            {
                CurrentStage = WorkflowStage.Idle,
                StageStartTime = DateTime.Now
            };

            // Přihlásit se k událostem
            _eventBus.Subscribe<CompilationCompletedEvent>(OnCompilationCompleted);
            _eventBus.Subscribe<FormAnalysisCompletedEvent>(OnFormAnalysisCompleted);
        }

        /// <summary>
        /// Spustí sekvenci vývoje aplikace
        /// </summary>
        public async Task StartDevelopmentSequenceAsync(string applicationDescription)
        {
            try
            {
                LogMessage($"Zahajuji vývojovou sekvenci pro: {applicationDescription}");

                _currentOperation = new DirigentOperation
                {
                    ApplicationDescription = applicationDescription,
                    ApplicationType = ApplicationType.WinForms,
                    StartTime = DateTime.Now
                };

                UpdateWorkflowState(WorkflowStage.Planning);

                // Fáze 1: Plánování
                await PlanDevelopmentAsync(applicationDescription);

                // Fáze 2: Generování kódu
                UpdateWorkflowState(WorkflowStage.Generation);
                _generatedCode = await _codeGenerator.GenerateCodeAsync(
                    applicationDescription,
                    _currentOperation.DevelopmentPlan,
                    ApplicationType.WinForms);
                _currentOperation.GeneratedCode = _generatedCode;

                // Fáze 3: Kompilace
                await CompileCodeAsync(_currentOperation);
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba v průběhu vývoje: {ex.Message}", MessageSeverity.Error);
                UpdateWorkflowState(WorkflowStage.Error);
            }
        }

        /// <summary>
        /// Plánování vývoje aplikace
        /// </summary>
        private async Task PlanDevelopmentAsync(string applicationDescription)
        {
            LogMessage("Plánuji kroky vývoje...");

            // Tady by byl kód pro generování plánu pomocí LLM
            // Pro jednoduchost vytvoříme základní plán přímo
            _currentOperation.DevelopmentPlan = $"Vytvořit Windows Forms aplikaci s popisem: {applicationDescription}";

            LogMessage("Plán vývoje vytvořen");
        }

        /// <summary>
        /// Kompilace kódu
        /// </summary>
        public async Task CompileCodeAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.Compilation);
            LogMessage("Kompiluji kód...");

            await _compilationEngine.CompileAsync(operation.GeneratedCode, operation.ApplicationType);
        }

        /// <summary>
        /// Handler pro dokončení kompilace
        /// </summary>
        private async void OnCompilationCompleted(CompilationCompletedEvent evt)
        {
            if (evt.Success)
            {
                LogMessage("Kompilace úspěšná");
                _compiledAssembly = evt.CompiledAssembly;
                await TestApplicationAsync();
            }
            else
            {
                LogMessage($"Kompilace selhala: {evt.Errors}", MessageSeverity.Error);
                await RepairCompilationErrorsAsync(evt.Errors);
            }
        }

        /// <summary>
        /// Handler pro dokončení analýzy formuláře
        /// </summary>
        private void OnFormAnalysisCompleted(FormAnalysisCompletedEvent evt)
        {
            LogMessage("Analýza formuláře dokončena");
            // Tady by mohlo být další zpracování výsledků analýzy
        }

        /// <summary>
        /// Testování aplikace
        /// </summary>
        private async Task TestApplicationAsync()
        {
            UpdateWorkflowState(WorkflowStage.Testing);
            LogMessage("Testuji aplikaci...");

            try
            {
                _runningForm = CreateFormInstance(_compiledAssembly);

                if (_runningForm == null)
                {
                    LogMessage("Nepodařilo se vytvořit instanci formuláře", MessageSeverity.Error);
                    await RepairCompilationErrorsAsync("Nepodařilo se vytvořit instanci formuláře");
                    return;
                }

                _eventBus.Publish(new FormCreatedEvent(_runningForm));

                string formDescription = await _formAnalyzer.AnalyzeFormAsync(_runningForm);
                LogMessage($"Formulář analyzován");
                _eventBus.Publish(new FormAnalysisCompletedEvent(formDescription));

                string testResult = await _codeGenerator.TestApplicationAsync(
                    formDescription,
                    _currentOperation.ApplicationDescription);

                bool testPassed = testResult.Contains("TEST PASS") || testResult.Contains("SUCCESS");
                _appMemory.SaveLearningTrace(_currentOperation.ApplicationDescription, "TestResult", testResult);

                if (testPassed)
                {
                    LogMessage("Testování úspěšné: " + testResult);
                    UpdateWorkflowState(WorkflowStage.Completed);
                }
                else
                {
                    LogMessage("Testování odhalilo problémy: " + testResult, MessageSeverity.Warning);
                    await RepairTestIssuesAsync(testResult);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při testování: {ex.Message}", MessageSeverity.Error);
                UpdateWorkflowState(WorkflowStage.Error);
            }
        }

        /// <summary>
        /// Oprava chyb kompilace
        /// </summary>
        private async Task RepairCompilationErrorsAsync(string errors)
        {
            UpdateWorkflowState(WorkflowStage.ErrorRepair);
            LogMessage($"Opravuji chyby kompilace...");

            _currentOperation.RepairAttempts++;

            if (_currentOperation.RepairAttempts > MaxRepairAttempts)
            {
                LogMessage($"Dosažen maximální počet pokusů o opravu ({MaxRepairAttempts})", MessageSeverity.Error);
                UpdateWorkflowState(WorkflowStage.Error);
                return;
            }

            string repairedCode = await _codeGenerator.RepairCodeAsync(
                _currentOperation.GeneratedCode,
                errors,
                "Opravte chyby kompilace");

            _currentOperation.GeneratedCode = repairedCode;
            _generatedCode = repairedCode;

            LogMessage($"Kód opraven (pokus #{_currentOperation.RepairAttempts}), zkouším znovu kompilovat");
            await CompileCodeAsync(_currentOperation);
        }

        /// <summary>
        /// Oprava problémů z testování
        /// </summary>
        private async Task RepairTestIssuesAsync(string testIssues)
        {
            UpdateWorkflowState(WorkflowStage.TestRepair);
            LogMessage($"Opravuji problémy z testování...");

            _currentOperation.RepairAttempts++;

            if (_currentOperation.RepairAttempts > MaxRepairAttempts)
            {
                LogMessage($"Dosažen maximální počet pokusů o opravu ({MaxRepairAttempts})", MessageSeverity.Error);
                UpdateWorkflowState(WorkflowStage.Error);
                return;
            }

            string repairedCode = await _codeGenerator.RepairTestIssuesAsync(
                _currentOperation.GeneratedCode,
                testIssues,
                "Opravte problémy odhalené při testování");

            _currentOperation.GeneratedCode = repairedCode;
            _generatedCode = repairedCode;

            LogMessage($"Kód opraven (pokus #{_currentOperation.RepairAttempts}), zkouším znovu kompilovat");
            await CompileCodeAsync(_currentOperation);
        }

        /// <summary>
        /// Vytvoření instance formuláře z assembly
        /// </summary>
        private Form CreateFormInstance(Assembly assembly)
        {
            try
            {
                var formTypes = assembly.GetTypes()
                    .Where(t => typeof(Form).IsAssignableFrom(t) && !t.IsAbstract)
                    .ToList();

                if (formTypes.Count == 0)
                {
                    LogMessage("V assembly nebyl nalezen žádný typ Form", MessageSeverity.Error);
                    return null;
                }

                // Najdeme hlavní formulář (většinou ten s Main metodou nebo první)
                var mainFormType = formTypes.FirstOrDefault(t =>
                    t.GetMethod("Main", System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public) != null) ?? formTypes.First();

                return (Form)Activator.CreateInstance(mainFormType);
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při vytváření instance formuláře: {ex.Message}", MessageSeverity.Error);
                return null;
            }
        }

        /// <summary>
        /// Řízení a učení z interakce s aplikací
        /// </summary>
        public async Task ControlAndLearnAsync()
        {
            if (_runningForm == null || _runningForm.IsDisposed)
            {
                LogMessage("Formulář není k dispozici pro ovládání", MessageSeverity.Warning);
                return;
            }

            LogMessage("Ovládám aplikaci a učím se z interakce...");

            // Získáme snapshot UI pro analýzu
            string uiSnapshot = _controller.ExtractUiTextSnapshot(_runningForm.Handle);
            _appMemory.SaveLearningTrace(_currentOperation.ApplicationDescription, "UiSnapshot", uiSnapshot);

            // Provedeme interakci s formulářem (tady by byla složitější implementace)
            bool interactionSuccess = await PerformUIInteractionAsync(uiSnapshot);

            if (interactionSuccess)
            {
                LogMessage("Interakce s formulářem úspěšná");
                UpdateWorkflowState(WorkflowStage.Completed);
            }
            else
            {
                LogMessage("Interakce s formulářem selhala", MessageSeverity.Warning);
                // Můžeme zkusit opravit kód nebo jinak reagovat
            }
        }

        /// <summary>
        /// Simulace interakce s UI
        /// </summary>
        private async Task<bool> PerformUIInteractionAsync(string uiSnapshot)
        {
            // Simulace interakce
            // Skutečná implementace by analyzovala UI a prováděla akce
            await Task.Delay(1000);
            return true;
        }

        /// <summary>
        /// Aktualizace stavu workflow
        /// </summary>
        private void UpdateWorkflowState(WorkflowStage newStage)
        {
            _currentState.PreviousStage = _currentState.CurrentStage;
            _currentState.CurrentStage = newStage;
            _currentState.StageStartTime = DateTime.Now;

            _eventBus.Publish(new WorkflowStateChangedEvent(_currentState));
        }

        /// <summary>
        /// Logování zprávy
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

            _eventBus.Publish(new SystemMessageEvent(systemMessage));
        }
    }

    /// <summary>
    /// Reprezentace operace prováděné dirigentem
    /// </summary>
    public class DirigentOperation
    {
        public string ApplicationDescription { get; set; }
        public ApplicationType ApplicationType { get; set; }
        public string DevelopmentPlan { get; set; }
        public string GeneratedCode { get; set; }
        public DateTime StartTime { get; set; }
        public int RepairAttempts { get; set; }
    }
}