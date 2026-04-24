using Microsoft.AspNetCore.Mvc;
using AmlaDeveloperAssistant.Services;
using AmlaDeveloperAssistantApp.Services;

namespace AmlaDeveloperAssistant.WebApi
{
    [ApiController]
    [Route("api/agent")]
    public class AgentController : ControllerBase
    {
        [HttpGet("setup")]
        public async Task<IActionResult> Setup()
        {
            string requestComputerName = Request.Headers["X-Computername"];
            string requestUsername = Request.Headers["X-Username"];

            if (string.IsNullOrEmpty(requestComputerName) || string.IsNullOrEmpty(requestUsername))
                return BadRequest(new { success = false, message = "Missing required headers: X-Computername and X-Username" });

            string jiraToken = Request.Headers["X-Jira-Token"];
            string gitToken = Request.Headers["X-Git-Token"];

            if (string.IsNullOrEmpty(jiraToken) || string.IsNullOrEmpty(gitToken))
                return BadRequest(new { success = false, message = "Missing required headers: X-Jira-Token and X-Git-Token" });

            string authHeader = Request.Headers["Authorization"].ToString();
            string authToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authHeader["Bearer ".Length..].Trim()
                : authHeader;

            await TokenEncryptionService.SaveTokensAsync(requestComputerName, requestUsername, jiraToken, gitToken, authToken);

            return Ok(new { success = true });
        }


        [HttpGet("isactive")]
        public async Task<IActionResult> IsActive()
        {
            var userProfile = Environment.CurrentDirectory;
            var repoVectorPath = Path.Combine(userProfile, "Vectors", "repo_vectors.json");
            var kbVectorPath = Path.Combine(userProfile, "Vectors", "kb_vectors.json");
            var chatHistoryPath = Path.Combine(userProfile, "Vectors", "chat_history.json");

            string _requestComputerName = Request.Headers["X-Computername"];
            string _requestUsername = Request.Headers["X-Username"];

            // Load and decrypt tokens from the per-machine token file
            (string _jiraToken, string _gitToken, string _authToken) = TokenEncryptionService
                .LoadTokensAsync(_requestComputerName, _requestUsername)
                .GetAwaiter()
                .GetResult();

            return Ok(new { active = $" {{ \"jiraToken\": \"{_jiraToken},\"gitToken\": \"{_gitToken},\"authToken\": \"{_authToken}\", \"kbVectorPath\": \"{kbVectorPath},{Path.Exists(kbVectorPath)}\" }}" });
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] QueryRequest req)
        {
            var userProfile = Environment.CurrentDirectory;
            var repoVectorPath = Path.Combine(userProfile, "Vectors", "repo_vectors.json");
            var kbVectorPath = Path.Combine(userProfile, "Vectors", "kb_vectors.json");
            var chatHistoryPath = Path.Combine(userProfile, "Vectors", "chat_history.json");

            var http = new HttpClient() { Timeout = Timeout.InfiniteTimeSpan };

            async Task<float[]> GetEmbedding(string text)
            {
                var reqObj = new
                {
                    model = "nomic-embed-text",
                    prompt = text.ToLower()
                };
                var res = await http.PostAsync(
                    "http://localhost:11434/api/embeddings",
                    new StringContent(System.Text.Json.JsonSerializer.Serialize(reqObj), System.Text.Encoding.UTF8, "application/json")
                );
                var json = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return json.RootElement
                    .GetProperty("embedding")
                    .EnumerateArray()
                    .Select(x => x.GetSingle())
                    .ToArray();
            }

            double Cosine(float[] a, float[] b)
            {
                int len = Math.Min(a.Length, b.Length);
                double dot = 0, magA = 0, magB = 0;
                for (int i = 0; i < len; i++)
                {
                    dot += a[i] * b[i];
                    magA += a[i] * a[i];
                    magB += b[i] * b[i];
                }
                return dot / (Math.Sqrt(magA) * Math.Sqrt(magB) + 1e-8);
            }

            var aiService = new AssistantAiService(
                repoVectorPath,
                kbVectorPath,
                chatHistoryPath,
                GetEmbedding,
                Cosine
            );
            var intentService = new IntentDetectionService(GetEmbedding, Cosine); 
            var fixService = new FixSuggestionService();

            string requestComputerName = Request.Headers["X-Computername"];
            string requestUsername = Request.Headers["X-Username"];
            string authHeader = Request.Headers["Authorization"].ToString();
            string authToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authHeader["Bearer ".Length..].Trim()
                : authHeader;

            var agent = new AgentOrchestrator(intentService, aiService, fixService, requestComputerName, requestUsername, authToken);

            var result = await agent.HandleQuery(req.Query);
            return Ok(new { answer = result });
        }
    }
}