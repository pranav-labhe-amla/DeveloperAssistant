using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistantApp.Services
{

    public class AssistantAiService
    {
        private readonly string _repoVectorPath;
        private readonly string _kbVectorPath;
        private readonly string _chatHistoryPath;
        private readonly Func<string, Task<float[]>> _getEmbedding;
        private readonly Func<float[], float[], double> _cosineSimilarity;

        public AssistantAiService(string repoVectorPath, string kbVectorPath, string chatHistoryPath, Func<string, Task<float[]>> getEmbedding, Func<float[], float[], double> cosineSimilarity)
        {
            _repoVectorPath = repoVectorPath;
            _kbVectorPath = kbVectorPath;
            _chatHistoryPath = chatHistoryPath;
            _getEmbedding = getEmbedding;
            _cosineSimilarity = cosineSimilarity;
        }

        public string ExtractTextFromADF(System.Text.Json.JsonElement adfElement)
        {
            if (adfElement.ValueKind == JsonValueKind.String)
                return adfElement.GetString();
            if (adfElement.ValueKind == JsonValueKind.Object && adfElement.TryGetProperty("content", out var content))
            {
                var sb = new StringBuilder();
                foreach (var item in content.EnumerateArray())
                {
                    sb.Append(ExtractTextFromADF(item));
                }
                return sb.ToString();
            }
            if (adfElement.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var item in adfElement.EnumerateArray())
                {
                    sb.Append(ExtractTextFromADF(item));
                }
                return sb.ToString();
            }
            if (adfElement.ValueKind == JsonValueKind.Object && adfElement.TryGetProperty("text", out var textProp))
                return textProp.GetString();
            return string.Empty;
        }

        public async Task<float[]> GetEmbedding(string text)
        {
            return await _getEmbedding(text);
        }

        public async Task<List<VectorChunk>> IndexRepository(string projectRoot, object? uiText, Action<string>? setUiMessage)
        {
            setUiMessage?.Invoke($"Indexing repository at {projectRoot}...");
            await Task.Delay(500); // Simulate work
            setUiMessage?.Invoke("Repository indexed.");
            return new List<VectorChunk>();
        }

        public async Task<List<VectorChunk>> IndexKnowledgeBase(object? uiText, Action<string>? setUiMessage)
        {
            setUiMessage?.Invoke("Indexing knowledge base...");
            await Task.Delay(500); // Simulate work
            setUiMessage?.Invoke("Knowledge base indexed.");
            return new List<VectorChunk>();
        }



        public async Task<string> BuildProjectContext(string projectRoot)
        {
            var context = new StringBuilder();
            try
            {
                if (Directory.Exists(projectRoot))
                {
                    context.AppendLine($"Project Root: {projectRoot}");
                    var projectFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
                    if (projectFiles.Length > 0)
                    {
                        context.AppendLine($"Found {projectFiles.Length} C# projects");
                    }
                    var srcDir = Path.Combine(projectRoot, "src");
                    if (Directory.Exists(srcDir))
                    {
                        var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
                        context.AppendLine($"Source files: {csFiles.Length}");
                    }
                }
            }
            catch { }
            return context.ToString();
        }

        public async Task<string?> ExtractJiraTicketIdAI(string input)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"[A-Z][A-Z0-9]+-\d+");
            await Task.CompletedTask;
            return match.Success ? match.Value : null;
        }

        // ... (rest of your methods: ChunkText, SearchVectors, SearchRepoVectors, SearchKBVectors, SearchHistoryVectors)

        // Place your existing methods here, inside the class body.

        public class VectorChunk
        {
            public string Source { get; set; }
            public string Content { get; set; }
            public float[] Embedding { get; set; }
        }
        public class ChatMessage
        {
            public string Role { get; set; }
            public string Content { get; set; }
        }

        public async Task<string> SearchVectors(string question, float[] queryVec)
        {
            var allChunks = new List<VectorChunk>();
            if (File.Exists(_repoVectorPath))
            {
                var repoJson = await File.ReadAllTextAsync(_repoVectorPath);
                var repo = JsonSerializer.Deserialize<List<VectorChunk>>(repoJson);
                if (repo != null) allChunks.AddRange(repo);
            }
            if (File.Exists(_kbVectorPath))
            {
                var kbJson = await File.ReadAllTextAsync(_kbVectorPath);
                var kb = JsonSerializer.Deserialize<List<VectorChunk>>(kbJson);
                if (kb != null) allChunks.AddRange(kb);
            }
            var ranked = allChunks
                .Select(v => new
                {
                    v.Content,
                    v.Source,
                    Score = _cosineSimilarity(queryVec, v.Embedding)
                })
                .Where(x => x.Score > 0.60)
                .OrderByDescending(x => x.Score)
                .Take(10)
                .ToList();
            if (!ranked.Any())
            {
                return "";
            }
            var context = new StringBuilder();
            foreach (var r in ranked)
            {
                context.AppendLine("----");
                context.AppendLine(r.Content);
            }
            var final = context.ToString();
            if (final.Length > 3000)
                final = final.Substring(0, 3000);
            return final;
        }

        public async Task<string> SearchRepoVectors(string question, int limit = 3000)
        {
            var allChunks = new List<VectorChunk>();
            if (File.Exists(_repoVectorPath))
            {
                var repoJson = await File.ReadAllTextAsync(_repoVectorPath);
                var repo = JsonSerializer.Deserialize<List<VectorChunk>>(repoJson);
                if (repo != null) allChunks.AddRange(repo);
            }
            var queryVec = await _getEmbedding(question);
            var ranked = allChunks
                .Select(v => new
                {
                    v.Content,
                    v.Source,
                    Score = _cosineSimilarity(queryVec, v.Embedding)
                })
                .Where(x => x.Score > 0.60)
                .OrderByDescending(x => x.Score)
                .Take(10)
                .ToList();
            if (!ranked.Any())
            {
                return "";
            }
            var context = new StringBuilder();
            foreach (var r in ranked)
            {
                context.AppendLine("----");
                context.AppendLine($"File:{r.Source}\nContent{r.Content}");
            }
            var final = context.ToString();
            if (final.Length > limit)
                final = final.Substring(0, limit);
            return $"{final}";
        }

        public async Task<string> SearchKBVectors(string question, int limit = 3000)
        {
            var allChunks = new List<VectorChunk>();
            if (File.Exists(_kbVectorPath))
            {
                var kbJson = await File.ReadAllTextAsync(_kbVectorPath);
                var kb = JsonSerializer.Deserialize<List<VectorChunk>>(kbJson);
                if (kb != null) allChunks.AddRange(kb);
            }
            var queryVec = await _getEmbedding(question);
            var ranked = allChunks
                .Select(v => new
                {
                    v.Content,
                    v.Source,
                    Score = _cosineSimilarity(queryVec, v.Embedding)
                })
                .Where(x => x.Score > 0.60)
                .OrderByDescending(x => x.Score)
                .Take(10)
                .ToList();
            if (!ranked.Any())
            {
                return "";
            }
            var context = new StringBuilder();
            foreach (var r in ranked)
            {
                context.AppendLine("----");
                context.AppendLine(r.Content);
            }
            var final = context.ToString();
            if (final.Length > limit)
                final = final.Substring(0, 3000);
            return final;
        }
    }
}
