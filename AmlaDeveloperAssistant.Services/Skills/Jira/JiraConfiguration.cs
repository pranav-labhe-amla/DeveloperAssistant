using System.IO;
using System.Text.Json;

namespace AmlaDeveloperAssistantApp.Services
{
    public class JiraConfiguration
    {
        public string BaseUrl { get; set; } = "https://amla.atlassian.net";
        public string Username { get; set; } = "";
        public string AuthToken { get; set; } = "";
        public string OllamaBaseUrl { get; set; } = "";

        public static JiraConfiguration Load(string path)
        {
            if (!File.Exists(path)) return new JiraConfiguration();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<JiraConfiguration>(json) ?? new JiraConfiguration();
        }

        public void Save(string path)
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(path, json);
        }
    }
}
