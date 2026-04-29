using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    public interface ISkillHandler
    {
        Task<string> ExecuteAsync(string input);
    }
}
