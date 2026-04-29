using AmlaDeveloperAssistantApp.Services;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    /// <summary>
    /// Evaluates intent signals and returns the <see cref="Skill"/> that
    /// should handle the query. Has no knowledge of how skills execute.
    /// </summary>
    public class SkillDetectionService
    {
        private readonly IntentDetectionService _intent;

        public SkillDetectionService(IntentDetectionService intent)
        {
            _intent = intent;
        }

        public async Task<Skill> DetectAsync(string input, float[] embedding)
        {
            if (await _intent.IsSimpleQueryVectorSearch(embedding))
                return Skill.Greeting;

            if (_intent.IsJiraTicketIntentRegx(input) || await _intent.IsJiraTicketIntentVectorSearch(embedding))
                return Skill.Jira;

            if (_intent.TryExtractPullRequestUrl(input, out _, out _, out _))
                return Skill.GitPullRequest;

            return Skill.KnowledgeBase;
        }
    }
}
