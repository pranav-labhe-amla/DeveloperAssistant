using System;
using System.IO;
using System.Text.Json;

namespace AmlaDeveloperAssistant.Services
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
                Username = "sample.user@amla.io",
                AuthToken = "ATATT3xFfGF0mJ9KGAm_Sample_User=63E2E4C3",
                OllamaBaseUrl = "http://localhost:11434"
            };
        }
    }
}
