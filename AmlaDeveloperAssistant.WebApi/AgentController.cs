using Microsoft.AspNetCore.Mvc;
using AmlaDeveloperAssistant.Services;
using AmlaDeveloperAssistantApp.Services;

namespace AmlaDeveloperAssistant.WebApi
{
    [ApiController]
    [Route("api/agent")]
    public class AgentController : ControllerBase
    {
        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] QueryRequest req)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var repoVectorPath = Path.Combine(userProfile, "AmlaDeveloperAssistant", "repo_vectors.json");
            var kbVectorPath = Path.Combine(userProfile, "AmlaDeveloperAssistant", "kb_vectors.json");
            var chatHistoryPath = Path.Combine(userProfile, "AmlaDeveloperAssistant", "chat_history.json");

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
            var jiraService = new JiraService(Constants.JiraBaseUrl, Constants.JiraUsername, Constants.JiraAuthToken);
            var fixService = new FixSuggestionService();

            var agent = new AgentOrchestrator(intentService, jiraService, aiService, fixService);

            var result = await agent.HandleQuery(req.Query);
            return Ok(new { answer = result });
        }
    }
}