using System.Threading.Tasks;

namespace AmlaDeveloperAssistantApp.Services
{
    public class PrReviewService
    {
        public Task<string> GetPRSuggestionsPromptForContext(string pullRequestContext, string pullRequest)
        {
            return Task.FromResult($@"
You are a senior architect and code analysis assistant reviewing a pull request.
Analyze the pull request and provide clear, code-focused guidance in plain English.

Pull Request: {pullRequest}

PULL REQUEST CONTEXT:
{pullRequestContext}

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
");
        }
    }
}
