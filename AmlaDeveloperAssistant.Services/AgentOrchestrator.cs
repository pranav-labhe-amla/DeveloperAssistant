using AmlaDeveloperAssistantApp.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public class AgentOrchestrator
    {
        private readonly AssistantAiService _ai;
        private readonly SkillDetectionService _skillDetection;
        private readonly Dictionary<Skill, ISkillHandler> _handlers;
        private readonly string _authToken;
        private readonly string _requestComputerName;
        private readonly string _requestUsername;

        public AgentOrchestrator(
            IntentDetectionService intent,
            AssistantAiService ai,
            FixSuggestionService fix,
            string requestComputerName,
            string requestUsername,
            string authToken)
        {
            _ai = ai;
            _requestComputerName = requestComputerName;
            _requestUsername     = requestUsername;

            // Load and decrypt all tokens from the per-machine secrets file
            var (jiraToken, gitToken, storedAuthToken) = TokenEncryptionService
                .LoadTokensAsync(_requestComputerName, _requestUsername)
                .GetAwaiter()
                .GetResult();

            _authToken = storedAuthToken;

            if (_authToken != authToken)
            {
                //ToDo: Handle unauthorized access properly, maybe with a custom exception and error response
            }

            var jiraEmail = string.IsNullOrEmpty(_requestUsername)
                ? Constants.JiraUsername
                : (_requestUsername.Contains("amla") ? _requestUsername : $"{_requestUsername}@amla.io");
             
            _skillDetection = new SkillDetectionService(intent);

            // ----------------------------------------------------------------
            // Skill registry — add a new skill by creating an ISkillHandler
            // and registering it here.
            // ----------------------------------------------------------------
            _handlers = new Dictionary<Skill, ISkillHandler>
            {
                [Skill.Greeting]       = new GreetingSkillHandler(),
                [Skill.Jira]           = new JiraSkillHandler(new JiraService(Constants.JiraBaseUrl, jiraEmail, jiraToken), ai, fix),
                [Skill.GitPullRequest] = new GitPullRequestSkillHandler(intent, new GitHubService(gitToken), new PrReviewService()),
                [Skill.KnowledgeBase]  = new KnowledgeBaseSkillHandler(ai)
            };
        }

        public async Task<string> HandleQuery(string input)
        {
            var embedding = await _ai.GetEmbedding(input);
            var skill = await _skillDetection.DetectAsync(input, embedding);

            return skill switch
            {
                Skill.Greeting       => await _handlers[Skill.Greeting].ExecuteAsync(input),
                Skill.Jira           => await _handlers[Skill.Jira].ExecuteAsync(input),
                Skill.GitPullRequest => await _handlers[Skill.GitPullRequest].ExecuteAsync(input),
                Skill.KnowledgeBase  => await _handlers[Skill.KnowledgeBase].ExecuteAsync(input),
                _                    => "I don't know."
            };
        }
    }
}
