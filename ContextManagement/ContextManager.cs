using System.Threading.Tasks;
using AutoKiwi.Translation; // Add this using directive

namespace AutoKiwi.ContextManagement
{
    /// <summary>
    /// Manages workflow and conversation context for prompt translation.
    /// </summary>
    public class ContextManager
    {
        public async Task<string> GetContextDataAsync(string conversationId, ContextScope scope)
        {
            // TODO: Implement context retrieval logic
            await Task.CompletedTask;
            return string.Empty;
        }

        public async Task AddMessageAsync(string conversationId, ConversationMessage message)
        {
            // TODO: Implement message storing logic
            await Task.CompletedTask;
        }
    }
}