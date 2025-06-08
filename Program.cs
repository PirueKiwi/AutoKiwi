// V Program.cs:

using AutoKiwi;
using System.CodeDom.Compiler;
using System.Windows.Forms;
using System;

[STAThread]
static void Main()
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    try
    {
        // Inicializace komponent
        var eventBus = new EventBus();
        var llmClient = new LmStudioClient(model: "codellama:13b");
        var appMemory = new SqliteApplicationMemory();
        var contextManager = new ContextManager(eventBus, appMemory);
        var promptTranslator = new PromptTranslator(eventBus, contextManager, appMemory);
        var codeGenerator = new CodeGenerator(eventBus, llmClient, promptTranslator);
        var compilationEngine = new CompilationEngine(eventBus);
        var formAnalyzer = new FormAnalyzer(eventBus);
        var win2TextController = new Win2TextController(eventBus);

        // Vytvoření dashboardu
        var dashboard = new MonitoringDashboard(eventBus);

        // Vytvoření dirigenta
        var dirigent = new Dirigent(
      eventBus,
      codeGenerator,
      compilationEngine,
      formAnalyzer,
      appMemory,
      win2TextController);

        // Vytvoření orchestrátoru
        var orchestrator = new WorkflowOrchestrator(dirigent, eventBus, dashboard);

        // Propojení dashboardu a dirigenta
        dashboard.SetDirigent(dirigent);

        // Informační zpráva o spuštění
        eventBus.Publish(new SystemMessageEvent(new SystemMessage
        {
            Message = "AutoKiwi aplikace byla spuštěna",
            Severity = MessageSeverity.Info,
            Timestamp = DateTime.Now,
            Source = "Program"
        }));

        // Spuštění dashboardu
        Application.Run(dashboard);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Aplikace se nemohla spustit: {ex.Message}", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}