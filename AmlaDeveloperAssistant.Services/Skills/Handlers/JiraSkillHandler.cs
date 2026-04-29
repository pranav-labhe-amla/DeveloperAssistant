using AmlaDeveloperAssistantApp.Services;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public class JiraSkillHandler : ISkillHandler
    {
        private readonly JiraService _jira;
        private readonly AssistantAiService _ai;
        private readonly FixSuggestionService _fix;

        public JiraSkillHandler(JiraService jira, AssistantAiService ai, FixSuggestionService fix)
        {
            _jira = jira;
            _ai   = ai;
            _fix  = fix;
        }

        public async Task<string> ExecuteAsync(string input)
        {
            try
            {
                var ticketId = await _ai.ExtractJiraTicketIdAI(input);

                if (string.IsNullOrEmpty(ticketId))
                    return "⚠️ Please provide a valid Jira ticket key (e.g., PROJ-123)";

                var ticket = await _jira.GetTicketAsync(ticketId);
                return await _fix.GetFixSuggestionsPromptForTicket(ticket, _ai, "");
            }
            catch(Exception ex) 
            {
                return $@"
You are a senior developer and code analysis assistant debugging a production issue.

If ticket details are missing or incomplete, ask for more information. If you have enough information, analyze the issue and suggest potential fixes.

RESPONSE FORMAT:
ISSUE_SUMMARY: [Brief summary]
AFFECTED_AREAS: [Modules/areas]
SUGGESTED_FILES: [Files needing changes]
METHODS_TO_CHECK: [Methods to examine]
FIX_TYPE: [Type of fix]
SUGGESTED_APPROACH: [Detailed approach]
PRIORITY_AREAS: [Focus areas]
";
            }
        }
    }
}
