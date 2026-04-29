using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistantApp.Services
{
    public class JiraService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _authToken;
        private readonly string _username;

        public JiraService(string baseUrl, string username, string authToken)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _authToken = authToken;

            _httpClient = new HttpClient();
            var byteArray = Encoding.ASCII.GetBytes(username+":"+ authToken);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// Search for Jira ticket by ticket key (e.g., "PROJ-123")
        /// </summary>
        public async Task<JiraTicket> GetTicketAsync(string ticketKey)
        {
            try
            {
                var url = $"{_baseUrl}/rest/api/3/issue/{ticketKey}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Jira API error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new JiraTicket
                {
                    Key = root.GetProperty("key").GetString() ?? "",
                    Summary = root.GetProperty("fields").GetProperty("summary").GetString() ?? "",
                    Description = ExtractDescription(root.GetProperty("fields")),
                    Status = root.GetProperty("fields").GetProperty("status").GetProperty("name").GetString() ?? "",
                    Priority = root.GetProperty("fields").GetProperty("priority").GetProperty("name").GetString() ?? "",
                    IssueType = root.GetProperty("fields").GetProperty("issuetype").GetProperty("name").GetString() ?? "",
                    BrowserUrl = $"{_baseUrl}/browse/{root.GetProperty("key").GetString()}"
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch ticket: {ex.Message}");
            }
        }

        /// <summary>
        /// Search for Jira tickets using JQL (Jira Query Language)
        /// </summary>
        public async Task<JiraTicket[]> SearchTicketsAsync(string jql, int maxResults = 10)
        {
            try
            {
                var url = $"{_baseUrl}/rest/api/3/search";
                var queryParams = $"?jql={Uri.EscapeDataString(jql)}&maxResults={maxResults}";
                
                var response = await _httpClient.GetAsync(url + queryParams);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Jira API error: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var issues = doc.RootElement.GetProperty("issues");

                var tickets = new System.Collections.Generic.List<JiraTicket>();

                foreach (var issue in issues.EnumerateArray())
                {
                    var key = issue.GetProperty("key").GetString() ?? "";
                    tickets.Add(new JiraTicket
                    {
                        Key = key,
                        Summary = issue.GetProperty("fields").GetProperty("summary").GetString() ?? "",
                        Description = ExtractDescription(issue.GetProperty("fields")),
                        Status = issue.GetProperty("fields").GetProperty("status").GetProperty("name").GetString() ?? "",
                        Priority = issue.GetProperty("fields").GetProperty("priority").GetProperty("name").GetString() ?? "",
                        IssueType = issue.GetProperty("fields").GetProperty("issuetype").GetProperty("name").GetString() ?? "",
                        BrowserUrl = $"{_baseUrl}/browse/{key}"
                    });
                }

                return tickets.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to search tickets: {ex.Message}");
            }
        }

        private string ExtractDescription(JsonElement fields)
        {
            string description = null;
            try
            {
                if (fields.TryGetProperty("description", out var descField) && descField.ValueKind != JsonValueKind.Null)
                {
                    // Handle ADF (Atlassian Document Format)
                    if (descField.TryGetProperty("content", out var content))
                    {
                        var sb = new StringBuilder();
                        foreach (var block in content.EnumerateArray())
                        {
                            ExtractTextFromADF(block, sb);
                        }
                        description = sb.ToString().Trim();
                    }
                    else
                    {
                        description = descField.GetString() ?? "";
                    }
                }
                if (fields.TryGetProperty("comment", out var commentField) && commentField.ValueKind != JsonValueKind.Null)
                {
                    description = description != null ? description + "\n comments:\n" : "";

                    var sb = new StringBuilder();
                    ExtractTextFromADF(commentField, sb);
                    description = description+sb.ToString().Trim();
                }
            }
            catch { }

            return description ?? "";
        }

        private void ExtractTextFromADF(JsonElement element, StringBuilder sb)
        {
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(element.GetRawText()));

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName
                        && reader.GetString() == "text"
                        && reader.Read()
                        && reader.TokenType == JsonTokenType.String)
                    {
                        sb.Append(reader.GetString());
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Get the browser URL for a ticket
        /// </summary>
        public string GetTicketUrl(string ticketKey)
        {
            return $"{_baseUrl}/browse/{ticketKey}";
        }
    }

    public class JiraTicket
    {
        public string Key { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public string Priority { get; set; } = "";
        public string IssueType { get; set; } = "";
        public string BrowserUrl { get; set; } = "";
    }
}
