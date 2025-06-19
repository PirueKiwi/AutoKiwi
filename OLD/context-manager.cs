using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoKiwi.Generation;
using AutoKiwi.Minimal;

namespace AutoKiwi.Translation
{
    /// <summary>
    /// Správce kontextu - udržuje kontext mezi iteracemi LLM
    /// Implementace podle PDF dokumentu
    /// </summary>
    public class ContextManager
    {
        private readonly IEventBus _eventBus;
        private readonly Dictionary<string, List<ConversationMessage>> _conversationContexts;
        private readonly int _defaultContextSize = 10; // Výchozí velikost kontextu
        private readonly int _maxContextTokens = 4000; // Maximální počet tokenů v kontextu
        
        public ContextManager(IEventBus eventBus)
        {
            _eventBus = eventBus;
            _conversationContexts = new Dictionary<string, List<ConversationMessage>>();
            
            // Přihlášení k událostem
            _eventBus.Subscribe<SystemMessageEvent>(OnSystemMessage);
            _eventBus.Subscribe<WorkflowStateChangedEvent>(OnWorkflowStateChanged);
        }
        
        /// <summary>
        /// Přidá zprávu do kontextu dané konverzace
        /// </summary>
        public async Task AddMessageAsync(string conversationId, ConversationMessage message)
        {
            if (string.IsNullOrEmpty(conversationId) || message == null)
            {
                return;
            }
            
            // Inicializace kontextu, pokud ještě neexistuje
            if (!_conversationContexts.ContainsKey(conversationId))
            {
                _conversationContexts[conversationId] = new List<ConversationMessage>();
            }
            
            // Přidání zprávy do kontextu
            _conversationContexts[conversationId].Add(message);
            
            // Ořezání kontextu, pokud je příliš velký
            TrimContextIfNeeded(conversationId);
            
            // Publikování události o změně kontextu
            _eventBus.Publish(new ContextChangedEvent(conversationId, message));
        }
        
        /// <summary>
        /// Získá relevantní data z kontextu pro danou konverzaci
        /// </summary>
        public async Task<string> GetContextDataAsync(string conversationId, ContextScope scope = ContextScope.Recent)
        {
            if (string.IsNullOrEmpty(conversationId) || !_conversationContexts.ContainsKey(conversationId))
            {
                return string.Empty;
            }
            
            var context = _conversationContexts[conversationId];
            
            // Výběr zpráv podle požadovaného rozsahu
            List<ConversationMessage> selectedMessages;
            
            switch (scope)
            {
                case ContextScope.Current:
                    // Pouze poslední zpráva
                    selectedMessages = context.Count > 0 ? new List<ConversationMessage> { context.Last() } : new List<ConversationMessage>();
                    break;
                    
                case ContextScope.Recent:
                    // Posledních N zpráv
                    selectedMessages = context.Skip(Math.Max(0, context.Count - _defaultContextSize)).ToList();
                    break;
                    
                case ContextScope.Relevant:
                    // Relevantní zprávy podle kontextu (implementujeme heuristiku)
                    selectedMessages = SelectRelevantMessages(context);
                    break;
                    
                case ContextScope.Complete:
                    // Všechny zprávy (ořezané podle maximálního počtu tokenů)
                    selectedMessages = context.ToList();
                    break;
                    
                default:
                    selectedMessages = context.Skip(Math.Max(0, context.Count - _defaultContextSize)).ToList();
                    break;
            }
            
            // Formátování kontextu do textové podoby
            return FormatMessagesAsContext(selectedMessages);
        }
        
        /// <summary>
        /// Sumarizuje kontext do stručné podoby s ohledem na maximální počet tokenů
        /// </summary>
        public async Task<string> SummarizeContextAsync(string conversationId, int maxTokens = 1000)
        {
            if (string.IsNullOrEmpty(conversationId) || !_conversationContexts.ContainsKey(conversationId))
            {
                return string.Empty;
            }
            
            var context = _conversationContexts[conversationId];
            
            if (context.Count == 0)
            {
                return string.Empty;
            }
            
            // Pro jednoduchou implementaci budeme předpokládat 4 znaky na token (hrubý odhad)
            int maxChars = maxTokens * 4;
            
            // Vytvoření sumarizace
            var summary = new StringBuilder();
            
            // Přidání informací o workflow
            var workflowMessages = context
                .Where(m => m.MessageType == MessageType.SystemMessage)
                .OrderByDescending(m => m.Timestamp)
                .Take(5);
                
            if (workflowMessages.Any())
            {
                summary.AppendLine("## Current Workflow State");
                foreach (var msg in workflowMessages)
                {
                    summary.AppendLine($"- {msg.Content}");
                }
                summary.AppendLine();
            }
            
            // Přidání posledních interakcí
            var lastInteractions = context
                .Where(m => m.MessageType != MessageType.SystemMessage)
                .OrderByDescending(m => m.Timestamp)
                .Take(3);
                
            if (lastInteractions.Any())
            {
                summary.AppendLine("## Recent Interactions");
                foreach (var msg in lastInteractions.Reverse())
                {
                    string content = TruncateContent(msg.Content, 200);
                    summary.AppendLine($"- **{msg.Role}**: {content}");
                }
                summary.AppendLine();
            }
            
            // Přidání shrnutí historie
            var allMessages = context.Count;
            var promptCount = context.Count(m => m.MessageType == MessageType.Prompt);
            var responseCount = context.Count(m => m.MessageType == MessageType.Response);
            
            summary.AppendLine("## Conversation Summary");
            summary.AppendLine($"- Total messages: {allMessages}");
            summary.AppendLine($"- Prompts sent: {promptCount}");
            summary.AppendLine($"- Responses received: {responseCount}");
            
            // Ořezání, pokud je sumarizace příliš dlouhá
            string result = summary.ToString();
            if (result.Length > maxChars)
            {
                result = result.Substring(0, maxChars) + "...";
            }
            
            return result;
        }
        
        /// <summary>
        /// Vybere relevantní zprávy z kontextu
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
        /// Formátuje zprávy do textové podoby kontextu
        /// </summary>
        private string FormatMessagesAsContext(List<ConversationMessage> messages)
        {
            if (messages.Count == 0)
            {
                return string.Empty;
            }
            
            var context = new StringBuilder();
            
            context.AppendLine("## Conversation Context");
            
            foreach (var message in messages)
            {
                string content = TruncateContent(message.Content, 500);
                
                switch (message.MessageType)
                {
                    case MessageType.Prompt:
                        context.AppendLine($"**Prompt ({message.Timestamp:HH:mm:ss})**: {content}");
                        break;
                        
                    case MessageType.Response:
                        context.AppendLine($"**Response ({message.Timestamp:HH:mm:ss})**: {content}");
                        break;
                        
                    case MessageType.SystemMessage:
                        context.AppendLine($"**System ({message.Timestamp:HH:mm:ss})**: {content}");
                        break;
                }
                
                context.AppendLine();
            }
            
            return context.ToString();
        }
        
        /// <summary>
        /// Zkrátí obsah zprávy na daný počet znaků
        /// </summary>
        private string TruncateContent(string content, int maxLength)
        {
            if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            {
                return content;
            }
            
            return content.Substring(0, maxLength) + "...";
        }
        
        /// <summary>
        /// Ořeže kontext, pokud je příliš velký
        /// </summary>
        private void TrimContextIfNeeded(string conversationId)
        {
            if (!_conversationContexts.ContainsKey(conversationId))
            {
                return;
            }
            
            var context = _conversationContexts[conversationId];
            
            // Jednoduchý odhad tokenů - 4 znaky na token
            int estimatedTokens = context.Sum(m => m.Content.Length) / 4;
            
            // Pokud je kontext příliš velký, ořežeme ho
            if (estimatedTokens > _maxContextTokens)
            {
                // Ponecháme první zprávu (pro inicializační kontext) a posledních N zpráv
                int keepCount = _defaultContextSize;
                
                if (context.Count > keepCount + 1)
                {
                    var newContext = new List<ConversationMessage>
                    {
                        context.First() // První zpráva
                    };
                    
                    // Přidáme posledních N zpráv
                    newContext.AddRange(context.Skip(context.Count - keepCount));
                    
                    _conversationContexts[conversationId] = newContext;
                }
            }
        }
        
        /// <summary>
        /// Event handler pro událost SystemMessageEvent
        /// </summary>
        private async void OnSystemMessage(SystemMessageEvent evt)
        {
            // Pro systémové zprávy vytvoříme umělé conversation ID
            string conversationId = "system";
            
            // Převod na konverzační zprávu
            var message = new ConversationMessage
            {
                Role = "system",
                Content = evt.Message.Message,
                Timestamp = evt.Message.Timestamp,
                MessageType = MessageType.SystemMessage
            };
            
            // Přidání do kontextu
            await AddMessageAsync(conversationId, message);
        }
        
        /// <summary>
        /// Event handler pro událost WorkflowStateChangedEvent
        /// </summary>
        private async void OnWorkflowStateChanged(WorkflowStateChangedEvent evt)
        {
            // Pro workflow zprávy vytvoříme umělé conversation ID
            string conversationId = "workflow";
            
            // Převod na konverzační zprávu
            var message = new ConversationMessage
            {
                Role = "system",
                Content = $"Workflow state changed from {evt.State.PreviousStage} to {evt.State.CurrentStage}",
                Timestamp = evt.State.StageStartTime,
                MessageType = MessageType.SystemMessage
            };
            
            // Přidání do kontextu
            await AddMessageAsync(conversationId, message);
        }
    }
    
    /// <summary>
    /// Událost publikovaná při změně kontextu
    /// </summary>
    public class ContextChangedEvent
    {
        public string ConversationId { get; }
        public ConversationMessage Message { get; }

        public ContextChangedEvent(string conversationId, ConversationMessage message)
        {
            ConversationId = conversationId;
            Message = message;
        }
    }
}