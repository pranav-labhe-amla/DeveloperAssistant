using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
            _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        public async Task<FixSuggestion> GetFixSuggestionsForTicket(JiraTicket ticket, AssistantAiService aiService, string projectRoot)
        {
            if (ticket == null || string.IsNullOrWhiteSpace(ticket.Description))
                return new FixSuggestion { Error = "❌ Cannot analyze ticket", IsSuccess = false };

            var projectContext = await aiService.BuildProjectContext(projectRoot);
            string kbContext = "", projContext = "";
            try
            {
                kbContext   = await aiService.SearchKBVectors(ticket.Description);
                projContext = await aiService.SearchRepoVectors(ticket.Description);
            }
            catch { }

            return await AnalyzeTicketAsync(
                ticket.Key,
                $"{ticket.Description}\n INFORMATION CONTEXT:{kbContext}\n",
                $"{projectContext}{projContext}"
            );
        }

        public async Task<string> GetFixSuggestionsPromptForTicket(JiraTicket ticket, AssistantAiService aiService, string projectRoot)
        {
            if (ticket == null || string.IsNullOrWhiteSpace(ticket.Description))
                return "❌ Cannot get the ticket";

            var projectContext = await aiService.BuildProjectContext(projectRoot);
            string kbContext = "", projContext = "";
            try
            {
                kbContext   = await aiService.SearchKBVectors(ticket.Description);
                projContext = await aiService.SearchRepoVectors(ticket.Description);
            }
            catch { }

            return BuildAnalysisPrompt(
                ticket.Key,
                $"{ticket.Description}\n INFORMATION CONTEXT:{kbContext}\n",
                $"{projectContext}{projContext}"
            );
        }

        public string FormatFixSuggestion(FixSuggestion suggestion)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"✅ FIX SUGGESTIONS FOR {suggestion.TicketKey}\n");
            sb.AppendLine("═══════════════════════════════════════\n");
            if (!string.IsNullOrWhiteSpace(suggestion.IssueSummary))     sb.AppendLine($"📌 Issue Summary:\n{suggestion.IssueSummary}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.FixType))          sb.AppendLine($"🔧 Fix Type: {suggestion.FixType}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.AffectedAreas))    sb.AppendLine($"📍 Affected Areas:\n{suggestion.AffectedAreas}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.SuggestedFiles))   sb.AppendLine($"📁 Suggested Files:\n{suggestion.SuggestedFiles}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.MethodsToCheck))   sb.AppendLine($"⚙️ Methods to Check:\n{suggestion.MethodsToCheck}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.PriorityAreas))    sb.AppendLine($"🎯 Priority Areas:\n{suggestion.PriorityAreas}\n");
            if (!string.IsNullOrWhiteSpace(suggestion.SuggestedApproach))sb.AppendLine($"💡 Suggested Approach:\n{suggestion.SuggestedApproach}\n");
            return sb.ToString();
        }

        public async Task<FixSuggestion> AnalyzeTicketAsync(string ticketKey, string description, string projectContext = "")
        {
            try
            {
                var prompt  = BuildAnalysisPrompt(ticketKey, description, projectContext);
                var request = new
                {
                    model   = "deepseek-coder:6.7b",
                    prompt,
                    stream  = false,
                    options = new { temperature = 0.3, num_predict = 1500 }
                };

                _currentCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                var response = await _httpClient.PostAsync(
                    $"{_ollamaBaseUrl}/api/generate",
                    new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
                    _currentCts.Token);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Ollama error: {response.StatusCode}");

                var doc          = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var responseText = doc.RootElement.GetProperty("response").GetString() ?? "";
                return ParseFixSuggestion(responseText, ticketKey);
            }
            catch (Exception ex)
            {
                return new FixSuggestion { TicketKey = ticketKey, Error = $"Failed to analyze: {ex.Message}", IsSuccess = false };
            }
        }

        private string BuildAnalysisPrompt(string ticketKey, string description, string projectContext) => $@"
You are a senior developer and code analysis assistant debugging a production issue.
Analyze the Jira ticket and provide clear, code-focused guidance on where to look, what to check, and how to fix the issue.

TICKET KEY: {ticketKey}
TICKET DESCRIPTION:
{description}
{(string.IsNullOrWhiteSpace(projectContext) ? "" : $"PROJECT CONTEXT:\n{projectContext}")}

RESPONSE FORMAT:
ISSUE_SUMMARY: [Brief summary]
AFFECTED_AREAS: [Modules/areas]
SUGGESTED_FILES: [Files needing changes]
METHODS_TO_CHECK: [Methods to examine]
FIX_TYPE: [Type of fix]
SUGGESTED_APPROACH: [Detailed approach]
PRIORITY_AREAS: [Focus areas]
";

        private FixSuggestion ParseFixSuggestion(string responseText, string ticketKey)
        {
            var suggestion = new FixSuggestion { TicketKey = ticketKey, IsSuccess = true, RawAnalysis = responseText };
            try
            {
                foreach (var line in responseText.Split(['\r', '\n'], StringSplitOptions.None))
                {
                    if      (line.StartsWith("ISSUE_SUMMARY:"))      suggestion.IssueSummary      = line["ISSUE_SUMMARY:".Length..].Trim();
                    else if (line.StartsWith("AFFECTED_AREAS:"))     suggestion.AffectedAreas     = line["AFFECTED_AREAS:".Length..].Trim();
                    else if (line.StartsWith("SUGGESTED_FILES:"))    suggestion.SuggestedFiles    = line["SUGGESTED_FILES:".Length..].Trim();
                    else if (line.StartsWith("METHODS_TO_CHECK:"))   suggestion.MethodsToCheck    = line["METHODS_TO_CHECK:".Length..].Trim();
                    else if (line.StartsWith("FIX_TYPE:"))           suggestion.FixType           = line["FIX_TYPE:".Length..].Trim();
                    else if (line.StartsWith("SUGGESTED_APPROACH:")) suggestion.SuggestedApproach = line["SUGGESTED_APPROACH:".Length..].Trim();
                    else if (line.StartsWith("PRIORITY_AREAS:"))     suggestion.PriorityAreas     = line["PRIORITY_AREAS:".Length..].Trim();
                }
                if (string.IsNullOrWhiteSpace(suggestion.IssueSummary))
                    suggestion.IssueSummary = responseText[..Math.Min(200, responseText.Length)];
            }
            catch { }
            return suggestion;
        }
    }

    public class FixSuggestion
    {
        public string TicketKey         { get; set; } = "";
        public bool   IsSuccess         { get; set; } = false;
        public string Error             { get; set; } = "";
        public string RawAnalysis       { get; set; } = "";
        public string IssueSummary      { get; set; } = "";
        public string AffectedAreas     { get; set; } = "";
        public string SuggestedFiles    { get; set; } = "";
        public string MethodsToCheck    { get; set; } = "";
        public string FixType           { get; set; } = "";
        public string SuggestedApproach { get; set; } = "";
        public string PriorityAreas     { get; set; } = "";
    }
}
