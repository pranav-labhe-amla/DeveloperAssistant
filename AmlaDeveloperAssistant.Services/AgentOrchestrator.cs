using AmlaDeveloperAssistantApp.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public class AgentOrchestrator
    {
        private readonly IntentDetectionService _intent;
        private readonly JiraService _jira;
        private readonly AssistantAiService _ai;
        private readonly FixSuggestionService _fix;

        public AgentOrchestrator(
            IntentDetectionService intent,
            JiraService jira,
            AssistantAiService ai,
            FixSuggestionService fix)
        {
            _intent = intent;
            _jira = jira;
            _ai = ai;
            _fix = fix;
        }

        public async Task<string> HandleQuery(string input)
        {
            var embedding = await _ai.GetEmbedding(input);

            // 1️⃣ Simple query (hi, hello, etc.)
            if (await _intent.IsSimpleQueryVectorSearch(embedding))
            {
                return "👋 Hello! How can I help you?";
            }

            // 2️⃣ Jira flow
            if (_intent.IsJiraTicketIntentRegx(input) || await _intent.IsJiraTicketIntentVectorSearch(embedding))
            {
                var ticketId = await _ai.ExtractJiraTicketIdAI(input);

                if (string.IsNullOrEmpty(ticketId))
                    return "⚠️ Please provide a valid Jira ticket key (e.g., PROJ-123)";

                var ticket = await _jira.GetTicketAsync(ticketId);

                var suggestion = await _fix.GetFixSuggestionsPromptForTicket(
                    ticket,
                    _ai,
                    "" // project root if needed
                );

                return suggestion;
            }

            // 3️⃣ Default → KB search (like "How checkout works?")
            var context = await _ai.SearchKBVectors(input);

            return string.IsNullOrWhiteSpace(context)
                ? "I don't know."
                : context;
        }
    }
}
