using System;
using System.IO;
using System.Text.Json;

namespace AmlaDeveloperAssistantApp.Services
{
    public class JiraConfiguration
    {
        public string BaseUrl { get; set; } = "";
        public string Username { get; set; } = "";
        public string AuthToken { get; set; } = "";
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AmlaDeveloperAssistant",
            "jira_config.json"
        );

        public static JiraConfiguration Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<JiraConfiguration>(json) 
                        ?? new JiraConfiguration();
                }
            }
            catch { }

            // Return default config
            return new JiraConfiguration
            {
                BaseUrl = "https://amla.atlassian.net",
                Username = "riya.agrawal@amla.io",
                AuthToken = "ATATT3xFfGF0mJ9KGAm_XMoI33reNrnaDema2uoHFNdLHNJLeyeha5zm7HZa5KGvdO4w25ezRtVyE-8ovlWBl90wUXfeevfKpuMu0Fkp7KsaK0qyRfFF7jXo2toZlA7IrVPyFdXrCME3CCrMDEmT0n3MzqCj0uJfoDh1XlEY_hX8TuvFkmr2cqI=63E2E4C3",
                OllamaBaseUrl = "http://localhost:11434"
            };
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
