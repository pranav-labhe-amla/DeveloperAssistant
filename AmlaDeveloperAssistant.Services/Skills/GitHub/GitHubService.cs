using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public class GitHubService
    {
        private readonly HttpClient _httpClient;

        public GitHubService(string gitToken)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AmlaDeveloperAssistant/1.0");
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            if (!string.IsNullOrWhiteSpace(gitToken))
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", gitToken);
        }

        public async Task<string> GetPullRequestContextAsync(string owner, string repo, string prNumber)
        {
            var prApiUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}";
            var prResponse = await _httpClient.GetAsync(prApiUrl);

            if (!prResponse.IsSuccessStatusCode)
                return $"Pull request URL detected: https://github.com/{owner}/{repo}/pull/{prNumber}. " +
                       $"Unable to fetch pull request details from GitHub API. Status: {(int)prResponse.StatusCode}.";

            using var prJson = JsonDocument.Parse(await prResponse.Content.ReadAsStringAsync());
            var root = prJson.RootElement;

            var title        = root.TryGetProperty("title",         out var p) ? p.GetString()   : string.Empty;
            var body         = root.TryGetProperty("body",          out p)     ? p.GetString()   : string.Empty;
            var state        = root.TryGetProperty("state",         out p)     ? p.GetString()   : string.Empty;
            var additions    = root.TryGetProperty("additions",     out p)     ? p.GetInt32()    : 0;
            var deletions    = root.TryGetProperty("deletions",     out p)     ? p.GetInt32()    : 0;
            var changedFiles = root.TryGetProperty("changed_files", out p)     ? p.GetInt32()    : 0;

            var fileChanges = await FetchFileChangesAsync(owner, repo, prNumber);

            var ctx = new StringBuilder();
            ctx.AppendLine($"Repository: {owner}/{repo}");
            ctx.AppendLine($"Pull Request: #{prNumber}");
            ctx.AppendLine($"State: {state}");
            ctx.AppendLine($"Title: {title}");

            if (!string.IsNullOrWhiteSpace(body))
            {
                ctx.AppendLine("Description:");
                ctx.AppendLine(body);
            }

            ctx.AppendLine($"Changed Files Count: {changedFiles}, Additions: {additions}, Deletions: {deletions}");

            if (fileChanges.Count > 0)
            {
                ctx.AppendLine("Files Changed:");
                foreach (var file in fileChanges)
                    ctx.AppendLine($"- {file.FileName} [{file.Status}] (+{file.Additions}/-{file.Deletions})");

                var patchBlocks = fileChanges
                    .FindAll(x => !string.IsNullOrWhiteSpace(x.Patch))
                    .GetRange(0, Math.Min(5, fileChanges.FindAll(x => !string.IsNullOrWhiteSpace(x.Patch)).Count));

                if (patchBlocks.Count > 0)
                {
                    ctx.AppendLine("Exact Change Blocks (truncated):");
                    foreach (var file in patchBlocks)
                    {
                        var snippet = file.Patch.Length > 1200 ? file.Patch[..1200] : file.Patch;
                        ctx.AppendLine($"File: {file.FileName}");
                        ctx.AppendLine(snippet);
                        ctx.AppendLine("----");
                    }
                }
            }

            return ctx.ToString();
        }

        private async Task<List<PrFileChange>> FetchFileChangesAsync(string owner, string repo, string prNumber)
        {
            var results = await FetchFilePageAsync(owner, repo, prNumber, page: 1);

            if (results.Count == 0)
                results = await FetchFilePageAsync(owner, repo, prNumber, page: 2);

            return results;
        }

        private async Task<List<PrFileChange>> FetchFilePageAsync(string owner, string repo, string prNumber, int page)
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}/files?per_page=30&page={page}";
            var response = await _httpClient.GetAsync(url);
            var list = new List<PrFileChange>();

            if (!response.IsSuccessStatusCode)
                return list;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var file in json.RootElement.EnumerateArray())
            {
                if (!file.TryGetProperty("filename", out var fileNameProp))
                    continue;

                var fileName = fileNameProp.GetString();
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                list.Add(new PrFileChange(
                    FileName:  fileName,
                    Status:    file.TryGetProperty("status",    out var s) ? s.GetString() ?? "modified" : "modified",
                    Additions: file.TryGetProperty("additions", out var a) ? a.GetInt32() : 0,
                    Deletions: file.TryGetProperty("deletions", out var d) ? d.GetInt32() : 0,
                    Patch:     file.TryGetProperty("patch",     out var pt) ? pt.GetString() ?? string.Empty : string.Empty
                ));
            }

            return list;
        }

        private record PrFileChange(string FileName, string Status, int Additions, int Deletions, string Patch);
    }
}
