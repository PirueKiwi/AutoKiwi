using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using AutoKiwi.Translation;
using AutoKiwi.Orchestration;

namespace AutoKiwi.Minimal
{
    /// <summary>
    /// Hlavní třída programu
    /// </summary>
    public class Program
    {
        // Globální synchronizační objekt pro čekání na dokončení celého procesu
        private static ManualResetEvent _processCompleted = new ManualResetEvent(false);
        private static Form _createdForm;

        [STAThread]
        static void Main(string[] args)
        {
            // Inicializace Windows Forms
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Console.WriteLine("AutoKiwi - Implementace s LM Studio");
            Console.WriteLine("====================================");

            // Vytvoření LM Studio klientů pro různé účely
            // Můžete upravit modely podle svých preferencí
            var generatorModel = new LmStudioClient(model: "codellama:13b");
            var testingModel = new LmStudioClient(model: "mistral:7b");

            // Vytvoření instancí hlavních komponent v UI vlákně
            var eventBus = new EventBus();
            var promptTranslator = new PromptTranslator(eventBus);
            var codeGenerator = new CodeGenerator(eventBus, generatorModel, promptTranslator);
            var compilationEngine = new CompilationEngine(eventBus);
            var formAnalyzer = new FormAnalyzer(eventBus);
            var appMemory = new ApplicationMemory(eventBus);

            var dirigent = new Dirigent(
                eventBus,
                codeGenerator,
                compilationEngine,
                formAnalyzer,
                appMemory);

            // Přihlásíme se k událostem - když se formulář vytvoří, nastavíme ho do globální proměnné
            eventBus.Subscribe<FormCreatedEvent>(evt => {
                _createdForm = evt.Form;
            });

            // Přihlásíme se k událostem dokončení
            eventBus.Subscribe<WorkflowStateChangedEvent>(evt => {
                if (evt.State.CurrentStage == WorkflowStage.Completed)
                {
                    Console.WriteLine("Vývoj aplikace byl úspěšně dokončen!");
                    // Signalizace dokončení
                    _processCompleted.Set();
                }
                else if (evt.State.CurrentStage == WorkflowStage.Error)
                {
                    Console.WriteLine("Vývoj aplikace selhal.");
                    // Signalizace dokončení
                    _processCompleted.Set();
                }
            });

            Console.WriteLine("Zadejte popis aplikace pro vygenerování:");
            string appDescription = Console.ReadLine();

            Console.WriteLine("\nSpouštím vývojovou sekvenci...");
            Console.WriteLine("Tento proces může trvat několik minut v závislosti na složitosti aplikace a rychlosti LLM.");
            Console.WriteLine("Prosím, čekejte...\n");

            // Spustíme vývojovou sekvenci ve vlákně na pozadí, aby UI thread nebyl blokovaný
            Task.Run(async () => {
                try
                {
                    await dirigent.StartDevelopmentSequenceAsync(appDescription);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Chyba: {ex.Message}");
                    _processCompleted.Set();
                }
            });

            // Čekáme na dokončení
            _processCompleted.WaitOne();
            Console.WriteLine("Proces dokončen!");

            // Po dokončení zobrazíme formulář
            if (_createdForm != null && !_createdForm.IsDisposed)
            {
                Console.WriteLine("Spouštím vygenerovanou aplikaci...");
                Application.Run(_createdForm);
            }
            else
            {
                Console.WriteLine("Formulář nebyl vytvořen nebo byl již odstraněn.");
                Console.WriteLine("Stiskněte libovolnou klávesu pro ukončení...");
                Console.ReadKey();
            }
        }
    }
}