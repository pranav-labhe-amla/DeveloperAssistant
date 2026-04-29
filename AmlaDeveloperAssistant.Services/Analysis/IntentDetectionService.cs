using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistantApp.Services
{
    public class IntentDetectionService
    {
        private readonly Func<string, Task<float[]>> _getEmbedding;
        private readonly Func<float[], float[], double> _cosineSimilarity;

        private static readonly Regex PullRequestUrlRegex = new(
            @"https:\/\/github\.com\/([\w\-\.]+)\/([\w\-\.]+)\/pull\/(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public IntentDetectionService(Func<string, Task<float[]>> getEmbedding, Func<float[], float[], double> cosineSimilarity)
        {
            _getEmbedding = getEmbedding;
            _cosineSimilarity = cosineSimilarity;
        }

        public async Task<bool> IsOpenSphereIntentVectorSearch(float[] questionEmbedding)
        {
            var openSphereExamples = new[]
            {
                "open znode sphere",
                "run sphere",
                "launch znode-sphere-tool",
                "start sphere",
                "open or run znode sphere tool"
            };
            var intentEmbeddings = new List<float[]>();
            foreach (var example in openSphereExamples)
            {
                intentEmbeddings.Add(await _getEmbedding(example));
            }
            foreach (var intentEmbedding in intentEmbeddings)
            {
                double similarity = _cosineSimilarity(questionEmbedding, intentEmbedding);
                if (similarity > 0.60)
                    return true;
            }
            return false;
        }

        public async Task<bool> IsSimpleQueryVectorSearch(float[] questionEmbedding)
        {
            var greetingExamples = new[]
            {
                "hi", "hello", "hey", "thanks", "thank you", "good morning", "good evening", "how are you", "cool", "ok"
            };
            var greetingEmbeddings = new List<float[]>();
            foreach (var example in greetingExamples)
            {
                greetingEmbeddings.Add(await _getEmbedding(example));
            }
            foreach (var greetingEmbedding in greetingEmbeddings)
            {
                double similarity = _cosineSimilarity(questionEmbedding, greetingEmbedding);
                if (similarity > 0.60)
                    return true;
            }
            return false;
        }

        public bool IsJiraTicketIntentRegx(string input)
        {
            var ticketKeyPattern = @"^[A-Z][A-Z0-9]+-\d+$";
            bool isJiraContentRegs = System.Text.RegularExpressions.Regex.IsMatch(input.ToUpper().Trim(), ticketKeyPattern);
            return isJiraContentRegs;
        }


        public async Task<bool> IsJiraTicketIntentVectorSearch(float[] questionEmbedding)
        {
            // Cosine similarity with regex pattern as a phrase
            var regexPattern = "jira ticket key pattern e.g. PROJ-123, BUG-456, FEAT-789";
            var regexEmbedding = await _getEmbedding(regexPattern);
            double regexSim = _cosineSimilarity(questionEmbedding, regexEmbedding);
            if (regexSim > 0.60)
                return true;

            var jiraExamples = new[]
            {
                "open jira ticket",
                "show jira issue",
                "fetch ticket",
                "get jira ticket",
                "jira ticket PROJ-123",
                "open or show a jira ticket",
                "mention of ticket keys like PROJ-123, BUG-456, FEAT-789"
            };
            var intentEmbeddings = new List<float[]>();
            foreach (var example in jiraExamples)
            {
                intentEmbeddings.Add(await _getEmbedding(example));
            }
            foreach (var intentEmbedding in intentEmbeddings)
            {
                double similarity = _cosineSimilarity(questionEmbedding, intentEmbedding);
                if (similarity > 0.60)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true and populates the out parameters when the input contains a GitHub PR URL.
        /// </summary>
        public bool TryExtractPullRequestUrl(string input, out string owner, out string repo, out string prNumber)
        {
            var match = PullRequestUrlRegex.Match(input);
            if (match.Success)
            {
                owner    = match.Groups[1].Value;
                repo     = match.Groups[2].Value;
                prNumber = match.Groups[3].Value;
                return true;
            }

            owner = repo = prNumber = string.Empty;
            return false;
        }
    }
}
