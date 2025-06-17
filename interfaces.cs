using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoKiwi.Memory;
using AutoKiwi.Orchestration;

namespace AutoKiwi.Minimal
{
    #region Rozhraní

    /// <summary>
    /// Rozhraní pro event bus
    /// </summary>
    public interface IEventBus
    {
        void Publish<TEvent>(TEvent evt);
        void Subscribe<TEvent>(Action<TEvent> handler);
        void Unsubscribe<TEvent>(Action<TEvent> handler);
    }

    /// <summary>
    /// Rozhraní pro generátor kódu
    /// </summary>
    public interface ICodeGenerator
    {
        Task<string> GenerateCodeAsync(string applicationDescription, string developmentPlan, ApplicationType applicationType);
        Task<string> RepairCodeAsync(string codeWithErrors, string errors, string repairStrategy);
        Task<string> TestApplicationAsync(string formDescription, string applicationDescription);
        Task<string> RepairTestIssuesAsync(string code, string testIssues, string repairStrategy);
    }

    /// <summary>
    /// Rozhraní pro kompilační engine
    /// </summary>
    public interface ICompilationEngine
    {
        Task CompileAsync(string sourceCode, ApplicationType appType);
    }

    /// <summary>
    /// Rozhraní pro analyzátor formulářů
    /// </summary>
    public interface IFormAnalyzer
    {
        Task<string> AnalyzeFormAsync(Form form);
    }

    /// <summary>
    /// Rozhraní pro paměť aplikací
    /// </summary>
    public interface IApplicationMemory
    {
        void SaveApplication(ApplicationProfile profile);
        Task<string> GetRelevantContext(string applicationDescription);
    }

    /// <summary>
    /// Rozhraní pro LLM klienta
    /// </summary>
    public interface ILlmClient
    {
        Task<string> GenerateAsync(string prompt);
    }

    #endregion

    #region Události

    /// <summary>
    /// Událost publikovaná při změně stavu workflow
    /// </summary>
    public class WorkflowStateChangedEvent
    {
        public WorkflowState State { get; }

        public WorkflowStateChangedEvent(WorkflowState state)
        {
            State = state;
        }
    }

    /// <summary>
    /// Událost publikovaná při dokončení kompilace
    /// </summary>
    public class CompilationCompletedEvent
    {
        public bool Success { get; }
        public Assembly CompiledAssembly { get; }
        public string Errors { get; }

        public CompilationCompletedEvent(bool success, Assembly compiledAssembly, string errors)
        {
            Success = success;
            CompiledAssembly = compiledAssembly;
            Errors = errors;
        }
    }

    /// <summary>
    /// Událost publikovaná při dokončení analýzy formuláře
    /// </summary>
    public class FormAnalysisCompletedEvent
    {
        public string FormDescription { get; }

        public FormAnalysisCompletedEvent(string formDescription)
        {
            FormDescription = formDescription;
        }
    }

    /// <summary>
    /// Událost publikovaná při vytvoření formuláře
    /// </summary>
    public class FormCreatedEvent
    {
        public Form Form { get; }

        public FormCreatedEvent(Form form)
        {
            Form = form;
        }
    }

    /// <summary>
    /// Událost publikovaná při systémové zprávě
    /// </summary>
    public class SystemMessageEvent
    {
        public SystemMessage Message { get; }

        public SystemMessageEvent(SystemMessage message)
        {
            Message = message;
        }
    }

    #endregion

    #region Modely

    /// <summary>
    /// Typ aplikace
    /// </summary>
    public enum ApplicationType
    {
        WinForms,
        Console
    }

    /// <summary>
    /// Závažnost zprávy
    /// </summary>
    public enum MessageSeverity
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Systémová zpráva
    /// </summary>
    public class SystemMessage
    {
        public string Message { get; set; }
        public MessageSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; }
        public string Source { get; set; }
    }

    /// <summary>
    /// Profil aplikace
    /// </summary>
 

    #endregion
}