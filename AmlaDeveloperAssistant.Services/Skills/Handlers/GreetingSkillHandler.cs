using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public class GreetingSkillHandler : ISkillHandler
    {
        public Task<string> ExecuteAsync(string input) =>
            Task.FromResult("👋 Hello! How can I help you?");
    }
}
