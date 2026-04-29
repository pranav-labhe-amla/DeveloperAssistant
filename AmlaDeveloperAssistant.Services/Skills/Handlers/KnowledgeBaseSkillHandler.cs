using AmlaDeveloperAssistantApp.Services;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public class KnowledgeBaseSkillHandler : ISkillHandler
    {
        private readonly AssistantAiService _ai;

        public KnowledgeBaseSkillHandler(AssistantAiService ai)
        {
            _ai = ai;
        }

        public async Task<string> ExecuteAsync(string input)
        {
            var context = await _ai.SearchKBVectors(input);
            return string.IsNullOrWhiteSpace(context) ? "I don't know." : context;
        }
    }
}
