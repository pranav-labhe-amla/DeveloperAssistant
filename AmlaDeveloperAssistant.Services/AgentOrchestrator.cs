using AmlaDeveloperAssistantApp.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public class AgentOrchestrator
    {
        private static readonly Regex PullRequestUrlRegex = new(
            @"https:\/\/github\.com\/([\w\-\.]+)\/([\w\-\.]+)\/pull\/(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private readonly IntentDetectionService _intent;
        private readonly JiraService _jira;
        private readonly AssistantAiService _ai;
        private readonly FixSuggestionService _fix;
        private readonly string _gitToken;
        private readonly string _jiraToken;
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
            _intent = intent;
            _requestComputerName = requestComputerName;
            _requestUsername = requestUsername;
            _ai = ai;
            _fix = fix;

            // Load and decrypt all tokens from the per-machine secrets file
            (_jiraToken, _gitToken, _authToken) = TokenEncryptionService
                .LoadTokensAsync(_requestComputerName, _requestUsername)
                .GetAwaiter()
                .GetResult();
            if(_authToken != authToken)
            {
                //ToDo: Handle unauthorized access properly, maybe with a custom exception and error response
            }
            //ToDo: This is for development only, remove the default token and enforce token input for security
            _gitToken = string.IsNullOrEmpty(_gitToken) ? "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" : _gitToken;
            _jiraToken = string.IsNullOrEmpty(_jiraToken) ? Constants.JiraAuthToken : _jiraToken;

            var jiraEmail = string.IsNullOrEmpty(_requestUsername)
                ? Constants.JiraUsername
                : (_requestUsername.Contains("amla") ? _requestUsername : $"{_requestUsername}@amla.io");

            _jira = new JiraService(
                Constants.JiraBaseUrl,
                jiraEmail,
                _jiraToken);
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

            // 2️⃣-b PR URL flow
            var prUrlMatch = PullRequestUrlRegex.Match(input);

            if (prUrlMatch.Success)
            {
                var owner = prUrlMatch.Groups[1].Value;
                var repo = prUrlMatch.Groups[2].Value;
                var prNumber = prUrlMatch.Groups[3].Value;

                var prContext = await GetPullRequestContext(owner, repo, prNumber); 

                var suggestion = await _fix.GetPRSuggestionsPromptForContext(prContext, $"https://github.com/{owner}/{repo}/pull/{prNumber}");
                  

                return suggestion;
            }

            // 3️⃣ Default → KB search (like "How checkout works?")
            var context = await _ai.SearchKBVectors(input);

            return string.IsNullOrWhiteSpace(context)
                ? "I don't know."
                : context;
        }

        private async Task<string> GetPullRequestContext(string owner, string repo, string prNumber)
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AmlaDeveloperAssistant/1.0");
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var token = _gitToken ?? "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";
            if (!string.IsNullOrWhiteSpace(token))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var prApiUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}";
            var prResponse = await httpClient.GetAsync(prApiUrl);

            if (!prResponse.IsSuccessStatusCode)
            {
                return $"Pull request URL detected: https://github.com/{owner}/{repo}/pull/{prNumber}. Unable to fetch pull request details from GitHub API. Status: {(int)prResponse.StatusCode}.";
            }

            using var prJson = JsonDocument.Parse(await prResponse.Content.ReadAsStringAsync());
            var root = prJson.RootElement;

            var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : string.Empty;
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : string.Empty;
            var state = root.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : string.Empty;
            var additions = root.TryGetProperty("additions", out var additionsProp) ? additionsProp.GetInt32() : 0;
            var deletions = root.TryGetProperty("deletions", out var deletionsProp) ? deletionsProp.GetInt32() : 0;
            var changedFiles = root.TryGetProperty("changed_files", out var changedFilesProp) ? changedFilesProp.GetInt32() : 0;

            var filesApiUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}/files?per_page=30";
            var filesResponse = await httpClient.GetAsync(filesApiUrl);
            var fileChanges = new List<(string FileName, string Status, int Additions, int Deletions, string Patch)>();

            if (filesResponse.IsSuccessStatusCode)
            {
                using var filesJson = JsonDocument.Parse(await filesResponse.Content.ReadAsStringAsync());
                foreach (var file in filesJson.RootElement.EnumerateArray())
                {
                    if (!file.TryGetProperty("filename", out var fileNameProp))
                    {
                        continue;
                    }

                    var fileName = fileNameProp.GetString();
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    var status = file.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "modified" : "modified";
                    var fileAdditions = file.TryGetProperty("additions", out var fileAdditionsProp) ? fileAdditionsProp.GetInt32() : 0;
                    var fileDeletions = file.TryGetProperty("deletions", out var fileDeletionsProp) ? fileDeletionsProp.GetInt32() : 0;
                    var patch = file.TryGetProperty("patch", out var patchProp) ? patchProp.GetString() ?? string.Empty : string.Empty;

                    fileChanges.Add((fileName, status, fileAdditions, fileDeletions, patch));
                }
            }

            if (fileChanges.Count == 0)
            {
                var pagedFilesApiUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}/files?per_page=30&page=2";
                var pagedFilesResponse = await httpClient.GetAsync(pagedFilesApiUrl);

                if (pagedFilesResponse.IsSuccessStatusCode)
                {
                    using var filesJson = JsonDocument.Parse(await pagedFilesResponse.Content.ReadAsStringAsync());
                    foreach (var file in filesJson.RootElement.EnumerateArray())
                    {
                        if (!file.TryGetProperty("filename", out var fileNameProp))
                        {
                            continue;
                        }

                        var fileName = fileNameProp.GetString();
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            continue;
                        }

                        var status = file.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "modified" : "modified";
                        var fileAdditions = file.TryGetProperty("additions", out var fileAdditionsProp) ? fileAdditionsProp.GetInt32() : 0;
                        var fileDeletions = file.TryGetProperty("deletions", out var fileDeletionsProp) ? fileDeletionsProp.GetInt32() : 0;
                        var patch = file.TryGetProperty("patch", out var patchProp) ? patchProp.GetString() ?? string.Empty : string.Empty;

                        fileChanges.Add((fileName, status, fileAdditions, fileDeletions, patch));
                    }
                }
            }

            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine($"Repository: {owner}/{repo}");
            contextBuilder.AppendLine($"Pull Request: #{prNumber}");
            contextBuilder.AppendLine($"State: {state}");
            contextBuilder.AppendLine($"Title: {title}");

            if (!string.IsNullOrWhiteSpace(body))
            {
                contextBuilder.AppendLine("Description:");
                contextBuilder.AppendLine(body);
            }

            contextBuilder.AppendLine($"Changed Files Count: {changedFiles}, Additions: {additions}, Deletions: {deletions}");

            if (fileChanges.Count > 0)
            {
                contextBuilder.AppendLine("Files Changed:");
                foreach (var file in fileChanges.Take(20))
                {
                    contextBuilder.AppendLine($"- {file.FileName} [{file.Status}] (+{file.Additions}/-{file.Deletions})");
                }

                var patchBlocks = fileChanges
                    .Where(x => !string.IsNullOrWhiteSpace(x.Patch))
                    .Take(5)
                    .ToList();

                if (patchBlocks.Count > 0)
                {
                    contextBuilder.AppendLine("Exact Change Blocks (truncated):");

                    foreach (var file in patchBlocks)
                    {
                        var patchSnippet = file.Patch.Length > 1200
                            ? file.Patch.Substring(0, 1200)
                            : file.Patch;

                        contextBuilder.AppendLine($"File: {file.FileName}");
                        contextBuilder.AppendLine(patchSnippet);
                        contextBuilder.AppendLine("----");
                    }
                }
            }

            return contextBuilder.ToString();
        }
    }
}
