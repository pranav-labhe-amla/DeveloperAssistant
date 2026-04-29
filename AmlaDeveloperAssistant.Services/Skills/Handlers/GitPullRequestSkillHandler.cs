using AmlaDeveloperAssistantApp.Services;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public class GitPullRequestSkillHandler : ISkillHandler
    {
        private readonly IntentDetectionService _intent;
        private readonly GitHubService _gitHub;
        private readonly PrReviewService _prReview;

        public GitPullRequestSkillHandler(IntentDetectionService intent, GitHubService gitHub, PrReviewService prReview)
        {
            _intent   = intent;
            _gitHub   = gitHub;
            _prReview = prReview;
        }

        public async Task<string> ExecuteAsync(string input)
        {
            try
            {
                _intent.TryExtractPullRequestUrl(input, out var owner, out var repo, out var prNumber);
                var prContext = await _gitHub.GetPullRequestContextAsync(owner, repo, prNumber);
                return await _prReview.GetPRSuggestionsPromptForContext(prContext, $"https://github.com/{owner}/{repo}/pull/{prNumber}");
            }
            catch
            {
                return $@"
You are a senior architect and code analysis assistant reviewing a pull request.
 
If the pull request URL is missing or invalid, but you have enough information about the changes, analyze the context and suggest potential issues, improvements, or areas to focus on in the review.

ANALYSIS TASK:
1. Do the code changes follow best practices and coding standards?
2. Are there potential bugs, logic errors, or missed edge cases?
3. Are there breaking changes or potential risks?
4. Are there specific improvements or optimizations to suggest?
5. Are there missing test cases or documentation updates?
6. Are there potential refactors that could be done?
7. Are there spelling or grammar mistakes in the description or code comments?

RESPONSE FORMAT:
IMPACTED_AREAS: [Affected modules/areas]
POTENTIAL_ISSUES: [Bugs, logic errors, or edge cases]
BEST_PRACTICES: [Standards not followed]
SUGGESTED_IMPROVEMENTS: [Improvements or optimizations]
TESTING_RECOMMENDATIONS: [Missing tests or docs]
POTENTIAL_RISKS: [Breaking changes or risks]
LOOKS_GOOD: [If no issues, say: The pull request looks good with no major issues detected.]

";
            }
        }
    }
}
