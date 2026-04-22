namespace AmlaDeveloperAssistantApp.Services
{
    public static class Constants
    {
        public const string JiraBaseUrl = "https://amla.atlassian.net";
        public const string JiraUsername = "pranav.labhe@amla.io";
        public const string JiraAuthToken = "ATATT3xFfGF0-f0vabMP76A2sabrZ0MUz6delnxT8fXshRjAe0r2zq9eO4pUdd7Vgu7jAF_1nnAq4P9dgkMSyxZlbRuRatDiIQwibOC4CcsaPIjISkxvRjKRJAUozcIANGVK1sNiB9d9WOS9wE0mX3b5AKDhnFlT14CSa2-D3a4jAeGWUl5tHEA=3C28ACAC";
        // UI Message Constants
        public const string Thinking = "Thinking...💭 ";
        public const string ThinkingDeeper = "⚡ Thinking deeper...🧠\n";
        public const string Error = "⚠️ Error: ";
        public const string JiraIntentError = "⚠️ Jira intent detection error: ";
        public const string CouldNotOpenBrowser = "⚠️ Could not open browser automatically. URL: ";
        public const string FailedToOpenJira = "❌ Failed to open Jira ticket: ";
        public const string FetchingJiraTicket = "🎫 Fetching Jira ticket: {0}";
        public const string LoadingTicket = "⏳ Loading ticket...";
        public const string LoadingTicketShort = "⏳ Loading ticket...";
        public const string AnalyzingTicket = "🧠 Analyzing ticket with AI...\n⏳ This may take a moment...";
        public const string CannotAnalyzeTicket = "❌ Cannot analyze ticket";
        public const string NoAnswerGenerated = "⚠️ No answer generated. Try refining your question.";
        public const string RequestTimedOut = "⚠️ Request timed out. Try again or refine your question.";
        public const string ProjectDirectoryNotFound = "Project directory not found.";
        public const string ProjectIndexBuilt = "Project index built. Chunks: {0}";
        public const string KBIndexBuilt = "KB index built. Chunks: {0}";
        public const string JiraNoTicketExtracted = "❌ I detected a Jira request, but couldn't extract a valid ticket ID.\n\nTry:\n• 'Show PROJ-123'\n• 'Open BUG-456'\n• 'Get FEAT-789'";
        public const string ErrorFetchingTicket = "❌ Error fetching ticket: {0}";
        public const string DisplayJiraTicket = "🎫 JIRA TICKET: {0}\n\n";
        public const string DisplaySummary = "📋 Summary: {0}\n";
        public const string DisplayOpenInBrowser = "🔗 Open in Browser";
        public const string DisplayStatus = "\n📊 Status: {0}";
        public const string DisplayPriority = "\n⚡ Priority: {0}";
        public const string DisplayType = "\n🏷️ Type: {0}\n\n";
        public const string DisplayDescription = "📝 Description:\n{0}";
        public const string AnalyzingTicketButton = "💡 Get Fix Suggestions for {0}";
        public const string ErrorGettingSuggestions = "❌ Error getting suggestions: {0}";
        public const string LaunchedSphereTool = "🌌 Launched znode-sphere-tool in a new command prompt.";
    }
}
