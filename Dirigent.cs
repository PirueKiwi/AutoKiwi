// Dirigent.cs - Klíčové části implementace
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoKiwi.Orchestration
{
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

        // Maximální počet pokusů
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
            _eventBus = eventBus;
            _codeGenerator = codeGenerator;
            _compilationEngine = compilationEngine;
            _formAnalyzer = formAnalyzer;
            _appMemory = appMemory;
            _controller = controller;

            _currentState = new WorkflowState
            {
                CurrentStage = WorkflowStage.Idle,
                StageStartTime = DateTime.Now
            };

            _eventBus.Subscribe<CompilationCompletedEvent>(OnCompilationCompleted);
            _eventBus.Subscribe<FormAnalysisCompletedEvent>(OnFormAnalysisCompleted);
        }

        // Hlavní metoda spuštění sekvence vývoje
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

                // 1. Fáze: Plánování
                await PlanDevelopmentAsync(applicationDescription);

                // 2. Fáze: Generování kódu
                UpdateWorkflowState(WorkflowStage.Generation);
                _generatedCode = await _codeGenerator.GenerateCodeAsync(
                    applicationDescription,
                    _currentOperation.DevelopmentPlan,
                    ApplicationType.WinForms);
                _currentOperation.GeneratedCode = _generatedCode;

                // 3. Fáze: Kompilace
                await CompileCodeAsync(_currentOperation);
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba v průběhu vývoje: {ex.Message}", MessageSeverity.Error);
                UpdateWorkflowState(WorkflowStage.Error);
            }
        }

        // Plánování vývoje
        private async Task PlanDevelopmentAsync(string applicationDescription)
        {
            LogMessage("Plánuji kroky vývoje...");

            // Tady by byl skutečný kód pro generování plánu pomocí LLM
            // Pro jednoduchost jen vytvoříme základní plán
            _currentOperation.DevelopmentPlan = $"Vytvořit Windows Forms aplikaci s popisem: {applicationDescription}";

            LogMessage("Plán vývoje vytvořen");
        }

        // Kompilace kódu
        public async Task CompileCodeAsync(DirigentOperation operation)
        {
            UpdateWorkflowState(WorkflowStage.Compilation);
            LogMessage("Kompiluji kód...");

            await _compilationEngine.CompileAsync(operation.GeneratedCode, operation.ApplicationType);
        }

        // Event handler pro dokončení kompilace
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

        // Testování aplikace
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
                LogMessage($"Formulář analyzován, popis: {formDescription.Substring(0, Math.Min(100, formDescription.Length))}...");
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

        // Oprava chyb kompilace
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

        // Oprava problémů z testování
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

        // Vytvoření instance formuláře
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

        // Řízení a učení
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

        // Simulace interakce s UI
        private async Task<bool> PerformUIInteractionAsync(string uiSnapshot)
        {
            // Tady by byla implementace pro analýzu UI a interakci
            // Pro jednoduchost vrátíme true
            await Task.Delay(1000); // Simulace práce
            return true;
        }

        // Aktualizace stavu workflow
        private void UpdateWorkflowState(WorkflowStage newStage)
        {
            _currentState.PreviousStage = _currentState.CurrentStage;
            _currentState.CurrentStage = newStage;
            _currentState.StageStartTime = DateTime.Now;

            _eventBus.Publish(new WorkflowStateChangedEvent(_currentState));
        }

        // Logování
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

    // Reprezentace operace prováděné dirigentem
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