using AmlaDeveloperAssistant.Services;
using AmlaDeveloperAssistantApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AmlaDeveloperAssistant.WebApi
{
    [ApiController]
    [Route("api/mcp")]
    public class McpController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> HandleMcp([FromBody] JsonElement body)
        {
            string? method = null;
            int id = 0;

            try
            {
                method = body.GetProperty("method").GetString();

                if (body.TryGetProperty("id", out var idElement))
                    id = idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt32() : 0;
            }
            catch
            {
                return Ok(new
                {
                    jsonrpc = "2.0",
                    id = 0,
                    error = new { code = -32600, message = "Invalid request" }
                });
            }

            // ✅ 1. Handle initialize handshake
            if (method == "initialize")
            {
                return Ok(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        protocolVersion = "2024-11-05",
                        serverInfo = new
                        {
                            name = "AmlaAssistant",
                            version = "1.0.0"
                        },
                        capabilities = new
                        {
                            tools = new { }
                        }
                    }
                });
            }

            // ✅ 2. Handle notifications/initialized (no response needed)
            if (method == "notifications/initialized")
            {
                return Ok();
            }

            // ✅ 3. Return available tools list
            if (method == "tools/list")
            {
                return Ok(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        tools = new[]
                        {
                            new
                            {
                                name = "askAgent",
                                description = "Send a query to the AmlaAssistant backend agent and get an answer",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        query = new
                                        {
                                            type = "string",
                                            description = "The user query to send to the backend agent"
                                        }
                                    },
                                    required = new[] { "query" }
                                }
                            }
                        }
                    }
                });
            }

            // ✅ 4. Handle tool execution
            if (method == "tools/call")
            {
                try
                {
                    var toolName = body.GetProperty("params").GetProperty("name").GetString();
                    var arguments = body.GetProperty("params").GetProperty("arguments");
                    var query = arguments.GetProperty("query").GetString() ?? "";

                    if (toolName == "askAgent")
                    {
                        var result = await ExecuteAgentQuery(query);
                        return Ok(new
                        {
                            jsonrpc = "2.0",
                            id,
                            result = new
                            {
                                content = new[]
                                {
                                    new { type = "text", text = result }
                                }
                            }
                        });
                    }

                    return Ok(new
                    {
                        jsonrpc = "2.0",
                        id,
                        error = new { code = -32601, message = $"Tool '{toolName}' not found" }
                    });
                }
                catch (Exception ex)
                {
                    return Ok(new
                    {
                        jsonrpc = "2.0",
                        id,
                        error = new { code = -32603, message = $"Tool execution error: {ex.Message}" }
                    });
                }
            }

            // ❌ Method not found
            return Ok(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32601, message = $"Method '{method}' not found" }
            });
        }

        private async Task<string> ExecuteAgentQuery(string query)
        {
            var userProfile = Environment.CurrentDirectory;
            var repoVectorPath = Path.Combine(userProfile, "Vectors", "repo_vectors.json");
            var kbVectorPath = Path.Combine(userProfile, "Vectors", "kb_vectors.json");
            var chatHistoryPath = Path.Combine(userProfile, "Vectors", "chat_history.json");

            var http = new HttpClient() { Timeout = Timeout.InfiniteTimeSpan };

            async Task<float[]> GetEmbedding(string text)
            {
                var reqObj = new { model = "nomic-embed-text", prompt = text.ToLower() };
                var res = await http.PostAsync(
                    "http://localhost:11434/api/embeddings",
                    new StringContent(JsonSerializer.Serialize(reqObj), System.Text.Encoding.UTF8, "application/json")
                );
                var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
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

            var aiService = new AssistantAiService(repoVectorPath, kbVectorPath, chatHistoryPath, GetEmbedding, Cosine);
            var intentService = new IntentDetectionService(GetEmbedding, Cosine);
            var fixService = new FixSuggestionService();
            string requestComputerName = Request.Headers["X-Computername"];
            string requestUsername = Request.Headers["X-Username"];
            
            string authHeader = Request.Headers["Authorization"].ToString();
            string authToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authHeader["Bearer ".Length..].Trim()
                : authHeader;

            var agent = new AgentOrchestrator(intentService, aiService, fixService, requestComputerName, requestUsername, authToken);

            return await agent.HandleQuery(query);
        }
    }
}