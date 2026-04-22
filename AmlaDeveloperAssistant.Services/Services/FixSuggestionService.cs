using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AmlaDeveloperAssistantApp.Services
{
    public class FixSuggestionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _ollamaBaseUrl;
        private CancellationTokenSource? _currentCts;

        public FixSuggestionService(string ollamaBaseUrl = "http://localhost:11434")
        {
            _ollamaBaseUrl = ollamaBaseUrl.TrimEnd('/');
            _httpClient = new HttpClient()
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }
        // Get fix suggestions for a Jira ticket
        public async Task<FixSuggestion> GetFixSuggestionsForTicket(JiraTicket ticket, AssistantAiService aiService, string projectRoot)
        {
            if (ticket == null || string.IsNullOrWhiteSpace(ticket.Description))
            {
                return new FixSuggestion { Error = "❌ Cannot analyze ticket", IsSuccess = false };
            }
            var projectContext = await aiService.BuildProjectContext(projectRoot);
            string kbcontext = "";
            string projcontext = "";
            try
            {
                kbcontext = await aiService.SearchKBVectors(ticket.Description);
                projcontext = await aiService.SearchRepoVectors(ticket.Description);
            }
            catch { }
            var suggestion = await AnalyzeTicketAsync(
                ticket.Key,
                $"{ticket.Description}\n INFORMATION CONTEXT:{kbcontext}\n",
                $"{projectContext}{projcontext}"
            );
            return suggestion;
        }

        // Get fix suggestions prompt for a Jira ticket
        public async Task<string> GetFixSuggestionsPromptForTicket(JiraTicket ticket, AssistantAiService aiService, string projectRoot)
        {
            if (ticket == null || string.IsNullOrWhiteSpace(ticket.Description))
            {
                return "❌ Cannot get the ticket";
            }
            var projectContext = await aiService.BuildProjectContext(projectRoot);
            string kbcontext = "";
            string projcontext = "";
            try
            {
                kbcontext = await aiService.SearchKBVectors(ticket.Description);
                projcontext = await aiService.SearchRepoVectors(ticket.Description);
            }
            catch { }
            var suggestion = BuildAnalysisPrompt(
                ticket.Key,
                $"{ticket.Description}\n INFORMATION CONTEXT:{kbcontext}\n",
                $"{projectContext}{projcontext}"
            );
            return suggestion;
        }

        // Display fix suggestions in formatted way
        public string FormatFixSuggestion(FixSuggestion suggestion)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"✅ FIX SUGGESTIONS FOR {suggestion.TicketKey}\n");
            sb.AppendLine("═══════════════════════════════════════\n");
            if (!string.IsNullOrWhiteSpace(suggestion.IssueSummary))
                sb.AppendLine($"📌 Issue Summary:\n{suggestion.IssueSummary}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.FixType))
                sb.AppendLine($"🔧 Fix Type: {suggestion.FixType}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.AffectedAreas))
                sb.AppendLine($"📍 Affected Areas:\n{suggestion.AffectedAreas}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.SuggestedFiles))
                sb.AppendLine($"📁 Suggested Files:\n{suggestion.SuggestedFiles}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.MethodsToCheck))
                sb.AppendLine($"⚙️ Methods to Check:\n{suggestion.MethodsToCheck}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.PriorityAreas))
                sb.AppendLine($"🎯 Priority Areas:\n{suggestion.PriorityAreas}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.SuggestedApproach))
                sb.AppendLine($"💡 Suggested Approach:\n{suggestion.SuggestedApproach}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Analyze a Jira ticket description and suggest files/methods/areas to fix using Ollama
        /// </summary>
        public async Task<FixSuggestion> AnalyzeTicketAsync(string ticketKey, string description, string projectContext = "")
        {
            try
            {
                var prompt = BuildAnalysisPrompt(ticketKey, description, projectContext);
                
                var request = new
                {
                    model = "deepseek-coder:6.7b",
                    prompt = prompt,
                    stream = false,
                    options = new
                    {
                        temperature = 0.3,
                        num_predict = 1500
                    }
                };

                _currentCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                var response = await _httpClient.PostAsync(
                    $"{_ollamaBaseUrl}/api/generate",
                    new StringContent(
                        JsonSerializer.Serialize(request),
                        Encoding.UTF8,
                        "application/json"
                    ),
                    _currentCts.Token
                );

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Ollama error: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var responseText = doc.RootElement.GetProperty("response").GetString() ?? "";

                return ParseFixSuggestion(responseText, ticketKey);
            }
            catch (Exception ex)
            {
                return new FixSuggestion
                {
                    TicketKey = ticketKey,
                    Error = $"Failed to analyze: {ex.Message}",
                    IsSuccess = false
                };
            }
        }

        private string BuildAnalysisPrompt(string ticketKey, string description, string projectContext)
        {
            //You are a code analysis assistant. Analyze the following Jira ticket and provide structured fix suggestions.
            return $@"
You are a senior developer and code analysis assistant debugging a production issue.
Analyze the Jira ticket, explain the problem and its impact, include relevant information from the provided KB context if available, and provide clear, code-focused guidance on where to look, what to check, and how to fix the issue.

TICKET KEY: {ticketKey}

TICKET DESCRIPTION:
{description}

{(string.IsNullOrWhiteSpace(projectContext) ? "" : $@"

PROJECT CONTEXT:
{projectContext}")}

ANALYSIS TASK:
Analyze the ticket and identify:
1. The main issue or bug described
2. Affected areas/modules
3. Specific files likely to need changes (estimate based on naming conventions)
4. Methods or functions that might need modification
5. Type of fix needed (bug fix, refactor, feature, performance improvement, etc.)

RESPONSE FORMAT:
Please structure your response as follows:

ISSUE_SUMMARY: [Brief summary of the issue]

AFFECTED_AREAS: [List affected modules/areas]

SUGGESTED_FILES: [List files likely needing changes]

METHODS_TO_CHECK: [List methods or functions to examine]

FIX_TYPE: [Type of fix]

SUGGESTED_APPROACH: [Detailed approach to fix]

PRIORITY_AREAS: [Areas to focus on first]

";
        }

        private FixSuggestion ParseFixSuggestion(string responseText, string ticketKey)
        {
            var suggestion = new FixSuggestion
            {
                TicketKey = ticketKey,
                IsSuccess = true,
                RawAnalysis = responseText
            };

            try
            {
                // Parse structured response
                var lines = responseText.Split(new[] { '\r', '\n' }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    if (line.StartsWith("ISSUE_SUMMARY:"))
                    {
                        suggestion.IssueSummary = line.Replace("ISSUE_SUMMARY:", "").Trim();
                    }
                    else if (line.StartsWith("AFFECTED_AREAS:"))
                    {
                        suggestion.AffectedAreas = line.Replace("AFFECTED_AREAS:", "").Trim();
                    }
                    else if (line.StartsWith("SUGGESTED_FILES:"))
                    {
                        suggestion.SuggestedFiles = line.Replace("SUGGESTED_FILES:", "").Trim();
                    }
                    else if (line.StartsWith("METHODS_TO_CHECK:"))
                    {
                        suggestion.MethodsToCheck = line.Replace("METHODS_TO_CHECK:", "").Trim();
                    }
                    else if (line.StartsWith("FIX_TYPE:"))
                    {
                        suggestion.FixType = line.Replace("FIX_TYPE:", "").Trim();
                    }
                    else if (line.StartsWith("SUGGESTED_APPROACH:"))
                    {
                        suggestion.SuggestedApproach = line.Replace("SUGGESTED_APPROACH:", "").Trim();
                    }
                    else if (line.StartsWith("PRIORITY_AREAS:"))
                    {
                        suggestion.PriorityAreas = line.Replace("PRIORITY_AREAS:", "").Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(suggestion.IssueSummary))
                {
                    suggestion.IssueSummary = responseText.Substring(0, Math.Min(200, responseText.Length));
                }
            }
            catch { }

            return suggestion;
        }
    }

    public class FixSuggestion
    {
        public string TicketKey { get; set; } = "";
        public bool IsSuccess { get; set; } = false;
        public string Error { get; set; } = "";
        public string RawAnalysis { get; set; } = "";

        public string IssueSummary { get; set; } = "";
        public string AffectedAreas { get; set; } = "";
        public string SuggestedFiles { get; set; } = "";
        public string MethodsToCheck { get; set; } = "";
        public string FixType { get; set; } = "";
        public string SuggestedApproach { get; set; } = "";
        public string PriorityAreas { get; set; } = "";
    }
}
