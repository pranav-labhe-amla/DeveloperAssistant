using HtmlAgilityPack;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;

using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace AmlaDeveloperAssistantApp
{
    class ChatMessage
    {
        public string Role { get; set; } // "user" / "assistant"
        public string Content { get; set; }
    }

    public partial class MainWindow : Window
    {
        private string projectRoot = @"D:\10x";
        private string jiraToken = "";
        private string jiraBaseUrl = "https://amla.atlassian.net";
        private string jiraEmail = "";

        // Load configuration from appsettings.json
        private void LoadJiraConfig()
        {
            try
            {
                var configPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "appsettings.json"
                );

                if (System.IO.File.Exists(configPath))
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    
                    var jiraSettings = doc.RootElement.GetProperty("JiraSettings");
                    jiraBaseUrl = jiraSettings.GetProperty("BaseUrl").GetString() ?? "https://amla.atlassian.net";
                    jiraEmail = jiraSettings.GetProperty("Email").GetString() ?? "";
                    jiraToken = jiraSettings.GetProperty("ApiToken").GetString() ?? "";
                    
                    System.Diagnostics.Debug.WriteLine($"✅ Loaded Jira config from: {configPath}");
                    System.Diagnostics.Debug.WriteLine($"Jira Base URL: {jiraBaseUrl}");
                    System.Diagnostics.Debug.WriteLine($"Jira Email: {jiraEmail}");
                    System.Diagnostics.Debug.WriteLine($"Jira Token: {(string.IsNullOrWhiteSpace(jiraToken) ? "NOT SET" : "[CONFIGURED]")}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Config file not found: {configPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading Jira config: {ex.Message}");
            }
        }

        // Extract Jira ticket ID from question
        private string? ExtractJiraTicketId(string question)
        {
            // Updated regex to match ticket IDs like Z10-32933, ABC-123, PROJ-456, etc.
            // Matches: (letters/digits)-digits
            var match = Regex.Match(question, @"\b([A-Z0-9]+-\d+)\b", RegexOptions.IgnoreCase);
            var ticketId = match.Success ? match.Groups[1].Value.ToUpper() : null;
            System.Diagnostics.Debug.WriteLine($"ExtractJiraTicketId - Input: '{question}' | Found: '{ticketId}'");
            return ticketId;
        }

        // AI-based intent detection for opening Jira tickets
        private async Task<bool> IsOpenJiraIntentAI(string question)
        {
            try
            {
                var prompt = $@"You are a query classifier. Respond with ONLY ONE WORD: OPENJIRA or OTHER.

OPENJIRA: opening/viewing/showing Jira tickets (any ticket ID like Z10-32933, ABC-123, etc.)
OTHER: anything else

Query: {question}

Response:";

                var req = new
                {
                    model = "phi3",
                    prompt = prompt,
                    stream = false,
                    options = new
                    {
                        temperature = 0.1,  // Very low for classification
                        top_p = 0.3,        // Reduce randomness
                        num_predict = 10    // Only expect 1-2 words
                    }
                };

                // Use a separate CancellationTokenSource for this operation
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    try
                    {
                        var res = await http.PostAsync(
                            "http://localhost:11434/api/generate",
                            new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json"),
                            cts.Token
                        );

                        if (!res.IsSuccessStatusCode)
                        {
                            var errorContent = await res.Content.ReadAsStringAsync();
                            System.Diagnostics.Debug.WriteLine($"IsOpenJiraIntentAI - HTTP Error: {res.StatusCode}");
                            System.Diagnostics.Debug.WriteLine($"IsOpenJiraIntentAI - Response: {errorContent}");
                            return false;
                        }

                        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

                        var output = json.RootElement.GetProperty("response")
                            .GetString()?.Trim().ToUpper();

                        var isJiraIntent = output == "OPENJIRA" || output == "OPENJIRA.";
                        System.Diagnostics.Debug.WriteLine($"IsOpenJiraIntentAI - Question: '{question}' | Response: '{output}' | Result: {isJiraIntent}");
                        
                        return isJiraIntent;
                    }
                    catch (OperationCanceledException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"IsOpenJiraIntentAI - Request timeout (20s): {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"⚠️ Ollama service may be slow or unresponsive at http://localhost:11434");
                        return false;
                    }
                    catch (HttpRequestException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"IsOpenJiraIntentAI - Connection error: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"⚠️ Cannot connect to Ollama at http://localhost:11434 - Is it running?");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IsOpenJiraIntentAI Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"IsOpenJiraIntentAI Stack: {ex.StackTrace}");
                return false;
            }
        }

        // Open Jira ticket in browser
        private void OpenJiraTicket(string ticketId)
        {
            try
            {
                var jiraUrl = $"{jiraBaseUrl}/browse/{ticketId}";
                
                System.Diagnostics.Debug.WriteLine($"Opening Jira URL: {jiraUrl}");
                
                // Method 1: Direct shell execute (works on Windows with URL protocol handlers)
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = jiraUrl,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    System.Diagnostics.Debug.WriteLine($"✅ Browser opened successfully via method 1");
                    return;
                }
                catch (Exception ex1)
                {
                    System.Diagnostics.Debug.WriteLine($"Method 1 failed: {ex1.Message}. Trying method 2...");
                }

                // Method 2: Use cmd.exe with proper quoting
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c start \"\" \"{jiraUrl}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    var process = System.Diagnostics.Process.Start(psi);
                    process?.WaitForExit(2000);
                    System.Diagnostics.Debug.WriteLine($"✅ Browser opened successfully via method 2 (cmd)");
                    return;
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"Method 2 failed: {ex2.Message}. Trying method 3...");
                }

                // Method 3: Use explorer.exe to open the URL
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = jiraUrl,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    System.Diagnostics.Debug.WriteLine($"✅ Browser opened successfully via method 3 (explorer)");
                    return;
                }
                catch (Exception ex3)
                {
                    System.Diagnostics.Debug.WriteLine($"Method 3 failed: {ex3.Message}. Trying method 4...");
                }

                // Method 4: Use control panel handler
                try
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = jiraUrl,
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    System.Diagnostics.Debug.WriteLine($"✅ Browser opened successfully via method 4");
                    return;
                }
                catch (Exception ex4)
                {
                    System.Diagnostics.Debug.WriteLine($"Method 4 failed: {ex4.Message}");
                }

                // If all methods fail
                AddAiMessage($"⚠️ Could not open browser automatically. URL: {jiraUrl}");
            }
            catch (Exception ex)
            {
                AddAiMessage($"❌ Failed to open Jira ticket: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error opening Jira: {ex}");
            }
        }

        // Fetch Jira ticket description using Jira API
        private async Task<string?> GetJiraTicketDescription(string ticketId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Fetching Jira ticket details for: {ticketId}");
                
                // Check if token is available
                if (string.IsNullOrWhiteSpace(jiraToken))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Jira API token not configured. Set JIRA_API_TOKEN environment variable.");
                    return null;
                }
                
                var apiUrl = $"{jiraBaseUrl}/rest/api/3/issue/{ticketId}?fields=summary,description,status,priority,assignee,customfield_10000,customfield_10001,customfield_10002,customfield_10003,customfield_10004,customfield_10005";
                
                using (var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, apiUrl))
                {
                    // Use Basic Auth with email and API token (required for Jira Cloud)
                    // Email and token are loaded from appsettings.json
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                    request.Headers.Add("Authorization", $"Basic {credentials}");
                    request.Headers.Add("Accept", "application/json");
                    
                    System.Diagnostics.Debug.WriteLine($"Making request to: {apiUrl}");
                    System.Diagnostics.Debug.WriteLine($"Auth: Basic [REDACTED]");  // Don't log actual credentials
                    
                    var response = await http.SendAsync(request);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"Jira API error: {response.StatusCode}");
                        System.Diagnostics.Debug.WriteLine($"Response: {errorContent}");
                        
                        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            System.Diagnostics.Debug.WriteLine("403 Forbidden - Check if the API token is valid and has appropriate permissions");
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            System.Diagnostics.Debug.WriteLine("401 Unauthorized - Check email and API token credentials");
                        }
                        
                        return null;
                    }
                    
                    var content = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"✅ Successfully fetched Jira response");
                    
                    var json = JsonDocument.Parse(content);
                    var fields = json.RootElement.GetProperty("fields");
                    
                    var summary = fields.GetProperty("summary").GetString() ?? "N/A";
                    
                    // Handle Jira Cloud's complex description format (ADF - Atlassian Document Format)
                    string description = "empty";
                    if (fields.TryGetProperty("description", out var descElement) && descElement.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        description = ExtractTextFromADF(descElement);
                    }
                    
                    // Extract RCA from custom fields - try multiple possible field IDs
                    string rca = "empty";
                    string[] possibleRCAFields = { "customfield_10000", "customfield_10001", "customfield_10002", "customfield_10003", "customfield_10004", "customfield_10005" };
                    
                    foreach (var fieldName in possibleRCAFields)
                    {
                        if (fields.TryGetProperty(fieldName, out var rcaElement) && rcaElement.ValueKind != System.Text.Json.JsonValueKind.Null)
                        {
                            var rcaText = ExtractTextFromADF(rcaElement);
                            if (!string.IsNullOrWhiteSpace(rcaText) && rcaText != "empty")
                            {
                                rca = rcaText;
                                System.Diagnostics.Debug.WriteLine($"✅ Found RCA in field: {fieldName}");
                                break;
                            }
                        }
                    }
                    
                    // If no custom field has RCA, try to extract from description if it contains RCA section
                    if (rca == "empty" && !string.IsNullOrWhiteSpace(description) && description != "empty")
                    {
                        // Check if description contains RCA section
                        var rcaMatch = Regex.Match(description, @"(?:Root Cause|RCA|Root Cause Analysis)[:\s]*(.+?)(?:\n\n|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        if (rcaMatch.Success)
                        {
                            rca = rcaMatch.Groups[1].Value.Trim();
                            // Limit RCA length and remove newlines
                            if (rca.Length > 500)
                            {
                                rca = rca.Substring(0, 500) + "...";
                            }
                            rca = Regex.Replace(rca, @"\s+", " ").Trim();
                            System.Diagnostics.Debug.WriteLine($"✅ Found RCA in description text");
                        }
                    }
                    
                    // Clean RCA if it still contains JSON or structured data
                    if (!string.IsNullOrWhiteSpace(rca) && rca != "empty" && (rca.Contains("{") || rca.Contains("[")))
                    {
                        // If it's JSON, set to empty
                        System.Diagnostics.Debug.WriteLine($"⚠️ RCA contains JSON data, skipping");
                        rca = "";
                    }
                    
                    var status = fields.TryGetProperty("status", out var statusElement) && statusElement.ValueKind != System.Text.Json.JsonValueKind.Null
                        ? statusElement.GetProperty("name").GetString() ?? "N/A"
                        : "N/A";
                    var priority = fields.TryGetProperty("priority", out var priorityElement) && priorityElement.ValueKind != System.Text.Json.JsonValueKind.Null
                        ? priorityElement.GetProperty("name").GetString() ?? "N/A"
                        : "N/A";
                    var assignee = fields.TryGetProperty("assignee", out var assigneeElement) && assigneeElement.ValueKind != System.Text.Json.JsonValueKind.Null
                        ? assigneeElement.GetProperty("displayName").GetString() ?? "Unassigned"
                        : "Unassigned";
                    
                    var jiraTicketUrl = $"{jiraBaseUrl}/browse/{ticketId}";
                    var ticketInfo = $@"
                    📋 **Jira Ticket: {ticketId}**
                    🔗 {jiraTicketUrl}
                    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                    **Summary:** {summary}
                    **Status:** {status}
                    **Priority:** {priority}
                    **Assignee:** {assignee}
                    **Description:**{description}
                    **Root Cause Analysis (RCA):**{rca}
                    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                    ";
                    
                    System.Diagnostics.Debug.WriteLine($"✅ Successfully fetched ticket details for {ticketId}");
                    return ticketInfo;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching Jira ticket: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        // Helper method to extract text from ADF format
        private string ExtractTextFromADF(JsonElement element)
        {
            try
            {
                var textParts = new List<string>();

                // Handle different JSON structures
                if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    // Simple string value
                    var plainText = element.GetString();
                    if (!string.IsNullOrWhiteSpace(plainText))
                    {
                        return plainText;
                    }
                }
                else if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    // Try to extract from ADF object format
                    if (element.TryGetProperty("content", out var contentArray) && 
                        contentArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        // First level: iterate through array
                        foreach (var item in contentArray.EnumerateArray())
                        {
                            // Try to get text directly
                            if (item.TryGetProperty("text", out var directText))
                            {
                                var text = directText.GetString();
                                if (!string.IsNullOrWhiteSpace(text))
                                    textParts.Add(text);
                            }

                            // Try nested content structure
                            if (item.TryGetProperty("content", out var nestedContent) && 
                                nestedContent.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var nestedItem in nestedContent.EnumerateArray())
                                {
                                    if (nestedItem.TryGetProperty("text", out var nestedText))
                                    {
                                        var text = nestedText.GetString();
                                        if (!string.IsNullOrWhiteSpace(text))
                                            textParts.Add(text);
                                    }
                                }
                            }
                        }
                    }
                    // Try direct "text" property
                    else if (element.TryGetProperty("text", out var directTextProp))
                    {
                        var text = directTextProp.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                    // Last resort: stringify the whole object
                    else
                    {
                        var rawJson = element.GetRawText();
                        if (!string.IsNullOrWhiteSpace(rawJson) && rawJson.Length < 1000)
                        {
                            return rawJson;
                        }
                    }
                }

                if (textParts.Count > 0)
                {
                    return string.Join(" ", textParts);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing ADF: {ex.Message}");
            }

            return "empty";
        }

        // AI-based intent detection for opening znode sphere
        private async Task<bool> IsOpenSphereIntentAI(string question)
        {
            try
            {
                var prompt = $@"
                Classify the user query intent.

                Return ONLY one word:
                OPENSPHERE or OTHER

                OPENSPHERE includes:
                - open znode sphere
                - run sphere
                - launch znode-sphere-tool
                - start sphere
                - any intent to open or run znode sphere tool

                Everything else is OTHER.

                Query:
                {question}
                ";

                var req = new
                {
                    model = "phi3",
                    prompt = prompt,
                    stream = false
                };

                // Use a separate CancellationTokenSource for this operation
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    try
                    {
                        var res = await http.PostAsync(
                            "http://localhost:11434/api/generate",
                            new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json"),
                            cts.Token
                        );

                        if (!res.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"IsOpenSphereIntentAI - HTTP Error: {res.StatusCode}");
                            return false;
                        }

                        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

                        var output = json.RootElement.GetProperty("response")
                            .GetString()?.Trim().ToUpper();

                        return output == "OPENSPHERE";
                    }
                    catch (OperationCanceledException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"IsOpenSphereIntentAI - Request timeout (20s): {ex.Message}");
                        return false;
                    }
                    catch (HttpRequestException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"IsOpenSphereIntentAI - Connection error: {ex.Message}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IsOpenSphereIntentAI Error: {ex.Message}");
                return false;
            }
        }

        private readonly string repoVectorPath;
        private readonly string kbVectorPath;
        private readonly string iconPath;

        private readonly HttpClient http = new HttpClient()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        private CancellationTokenSource? _currentCts;
        private bool _isProcessing = false;
        private bool _isResponseGenerating = false;
        private List<ChatMessage> _chatHistory = new();

        private readonly string chatHistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AmlaDeveloperAssistant",
            "chat_history.json"
        );

        public MainWindow()
        {
            InitializeComponent();

            // Load Jira configuration
            LoadJiraConfig();

            selectedpath.Text = projectRoot;

            repoVectorPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AmlaDeveloperAssistant",
                "repo_vectors.json"
            );

            kbVectorPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AmlaDeveloperAssistant",
                "kb_vectors.json"
            );

            iconPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "app.ico"
            );

            // Set window icon if it exists
            if (File.Exists(iconPath))
            {
                this.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));
            }

            if (File.Exists(chatHistoryPath))
            {
                var json = File.ReadAllText(chatHistoryPath);
                _chatHistory = JsonSerializer.Deserialize<List<ChatMessage>>(json) ?? new();
            }
            // position bottom right
            var area = Screen.PrimaryScreen.WorkingArea;
            Left = area.Width - Width - 10;
            Top = area.Height - Height - 10;

            // fire and forget (non-blocking)
            var bubble = AddAiMessage("🔄 Initializing AI...");
            var textBlock = (System.Windows.Controls.RichTextBox)bubble.Child;


            WarmUpModels(textBlock);

        }
        private async Task WarmUpModels(System.Windows.Controls.RichTextBox? uiText = null)
        {
            SendButton.IsEnabled = false;
            QuestionBox.IsEnabled = false;
            var olderForeground = SendButton.Foreground;
            SendButton.Foreground = Brushes.BlueViolet;
            if (uiText != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    SetUIMessage(uiText, "🔄 Warming up AI models...\nThis may take a moment, but it ensures faster responses later on.\n");
                });
            }
            try
            {
                var textModels = new[]
                {
                "phi3",
                "deepseek-coder:6.7b"
            };

                var embeddingModels = new[]
                {
                "nomic-embed-text"
            };

                // 🔹 Warm text models
                foreach (var model in textModels)
                {
                    try
                    {
                        UpdateUI(uiText, $"\n⏳ Loading model: {model}...");

                        var req = new
                        {
                            model = model,
                            keep_alive = -1
                        };

                        await http.PostAsync(
                            "http://localhost:11434/api/generate",
                            new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json")
                        );

                        UpdateUI(uiText, $"\n✅ Model ready: {model}");
                    }
                    catch
                    {
                        UpdateUI(uiText, $"\n❌ Failed: {model}");
                    }
                }

                // 🔹 Warm embedding models (IMPORTANT CHANGE)
                foreach (var model in embeddingModels)
                {
                    try
                    {
                        UpdateUI(uiText, $"\n⏳ Loading embedding model: {model}...");

                        var req = new
                        {
                            model = model,
                            prompt = "warmup"
                        };

                        await http.PostAsync(
                            "http://localhost:11434/api/embeddings",
                            new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json")
                        );

                        UpdateUI(uiText, $"\n✅ Embedding model ready: {model}");
                    }
                    catch
                    {
                        UpdateUI(uiText, $"\n❌ Embedding failed: {model}");
                    }
                }

                UpdateUI(uiText, "\n🚀 All models warmed up!");
                UpdateUI(uiText, "\n\nYou can now ask your questions. Feel free to explore! 😊");
            }
            catch (Exception ex)
            {
                UpdateUI(uiText, "\n⚠️ Warm-up error: " + ex.Message);
            }
            finally
            {
                SendButton.IsEnabled = true;
                QuestionBox.IsEnabled = true;
                SendButton.Foreground = olderForeground;
                QuestionBox.Focus();
            }
        }

        private void UpdateUI(System.Windows.Controls.RichTextBox? uiText, string message)
        {
            if (uiText == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AppendUIMessage(uiText, message);
            });
        }

        private void SetUpdateUIMessage(System.Windows.Controls.RichTextBox? uiText, string message)
        {
            if (uiText == null) return;
            SetUIMessage(uiText, message);
        }

        private void SaveChatHistory()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(chatHistoryPath)!);

            File.WriteAllText(
                chatHistoryPath,
                JsonSerializer.Serialize(_chatHistory)
            );
        }


        class VectorChunk
        {
            public string Source { get; set; }
            public string Content { get; set; }
            public float[] Embedding { get; set; }
        }

        private void QuestionBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                OnSend(sender, e);
        }

        private void QuestionBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Submit on Enter (without Shift)
            if (e.Key == Key.Enter)
            {
                // If Shift is pressed, allow new line.
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    return; // Allow Shift+Enter for new line
                }

                // Enter alone sends the message
                OnSend(sender, e);
                e.Handled = true;
            }
        }

        private void AddUserMessage(string text)
        {
            // Use TextBox instead of TextBlock to allow text selection and copying
            var textBox = new System.Windows.Controls.TextBox
            {
                Text = text,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 240,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                IsEnabled = true // Allow selection even though read-only
            };

            var bubble = new Border
            {
                Background = Brushes.DodgerBlue,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(60, 5, 5, 5),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Child = textBox
            };

            ChatPanel.Children.Add(bubble);
            ChatScroll.ScrollToEnd();
        }

        private Border AddAiMessage(string text)
        {
            var richTextBox = new System.Windows.Controls.RichTextBox
            {
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                IsDocumentEnabled = true, // enables hyperlink click
                MaxWidth = 240,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden
            };

            var document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent
            };

            // Parse lines and handle markdown formatting
            var lines = text.Split(new[] { "\n", Environment.NewLine }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                var paragraph = new Paragraph();
                var trimmedLine = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    document.Blocks.Add(paragraph);
                    continue;
                }

                // Check for bold text pattern: **text**
                var boldPattern = new Regex(@"\*\*([^*]+)\*\*");
                var urlRegex = new Regex(@"(https?://[\w\-._~:/?#[\]@!$&'()*+,;=%]+)");
                
                int lastIndex = 0;
                var matches = new List<(int Index, int Length, string Text, bool IsBold, bool IsUrl)>();

                // Collect all matches
                foreach (Match match in boldPattern.Matches(trimmedLine))
                {
                    matches.Add((match.Index, match.Length, match.Groups[1].Value, true, false));
                }

                foreach (Match match in urlRegex.Matches(trimmedLine))
                {
                    // Check if this URL is not already inside a bold match
                    bool isInsideBold = matches.Any(m => m.IsBold && match.Index >= m.Index && match.Index + match.Length <= m.Index + m.Length);
                    if (!isInsideBold)
                    {
                        matches.Add((match.Index, match.Length, match.Value, false, true));
                    }
                }

                // Sort matches by index
                matches = matches.OrderBy(m => m.Index).ToList();

                lastIndex = 0;
                foreach (var match in matches)
                {
                    // Add text before the match
                    if (match.Index > lastIndex)
                    {
                        var beforeText = trimmedLine.Substring(lastIndex, match.Index - lastIndex);
                        paragraph.Inlines.Add(new Run(beforeText));
                    }

                    if (match.IsUrl)
                    {
                        // Add hyperlink
                        var link = new Hyperlink(new Run(match.Text))
                        {
                            NavigateUri = new Uri(match.Text),
                            Foreground = Brushes.Cyan
                        };
                        link.RequestNavigate += (s, e) =>
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                        };
                        paragraph.Inlines.Add(link);
                    }
                    else if (match.IsBold)
                    {
                        // Add bold text
                        var boldRun = new Run(match.Text)
                        {
                            FontWeight = System.Windows.FontWeights.Bold,
                            Foreground = Brushes.Yellow
                        };
                        paragraph.Inlines.Add(boldRun);
                    }

                    lastIndex = match.Index + match.Length;
                }

                // Add remaining text
                if (lastIndex < trimmedLine.Length)
                {
                    var remainingText = trimmedLine.Substring(lastIndex);
                    // Remove any leftover ** markers
                    remainingText = remainingText.Replace("**", "");
                    if (!string.IsNullOrEmpty(remainingText))
                    {
                        paragraph.Inlines.Add(new Run(remainingText));
                    }
                }

                paragraph.Margin = new Thickness(0);
                document.Blocks.Add(paragraph);
            }

            richTextBox.Document = document;

            var bubble = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(5, 5, 60, 5),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Child = richTextBox
            };

            ChatPanel.Children.Add(bubble);
            ChatScroll.ScrollToEnd();

            return bubble;
        }

        // SEND QUESTION
        private async void OnSend(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                try
                {
                    _currentCts?.Cancel();
                }
                catch { }

                return;
            }

            if (_isResponseGenerating)
            {
                try
                {
                    _isResponseGenerating = false;
                }
                catch { }

                return;
            }

            var question = QuestionBox.Text;

            if (string.IsNullOrWhiteSpace(question))
                return;

            // Disable UI
            _isProcessing = true;
            SetUiEnabled(false);
            try
            {
                AddUserMessage(question);
                QuestionBox.Text = "";

                System.Diagnostics.Debug.WriteLine($"\n=== OnSend Debug ===\nQuestion: '{question}'");

                // Check for Jira ticket intent FIRST (before adding AI bubble)
                var ticketId = ExtractJiraTicketId(question);
                System.Diagnostics.Debug.WriteLine($"Extracted Ticket ID: {ticketId}");
                
                if (ticketId != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Ticket ID found. Checking AI intent...");
                    var isJiraIntent = await IsOpenJiraIntentAI(question);
                    System.Diagnostics.Debug.WriteLine($"IsOpenJiraIntentAI returned: {isJiraIntent}");
                    
                    if (isJiraIntent)
                    {
                        System.Diagnostics.Debug.WriteLine($"Opening Jira ticket: {ticketId}");
                        OpenJiraTicket(ticketId);
                        
                        // If token is not configured, just show browser message
                        if (string.IsNullOrWhiteSpace(jiraToken))
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ Jira API token not configured. Token value: '{jiraToken}'");
                            var jiraUrl = $"{jiraBaseUrl}/browse/{ticketId}";
                            AddAiMessage($"🔗 Opened Jira ticket in browser: {ticketId}");
                            return;
                        }
                        
                        // Fetch and display ticket description
                        var ticketDescription = await GetJiraTicketDescription(ticketId);
                        if (ticketDescription != null)
                        {
                            AddAiMessage(ticketDescription);
                        }
                        else
                        {
                            AddAiMessage($"🔗 Opened Jira ticket: {ticketId}");
                        }
                        return;
                    }
                }

                // Check for open sphere intent using AI
                if (await IsOpenSphereIntentAI(question))
                {
                    // Launch znode-sphere-tool in a new command prompt
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/k znode-sphere-tool",
                        UseShellExecute = true
                    });
                    AddAiMessage("🌌 Launched znode-sphere-tool in a new command prompt.");
                    return;
                }

                // Only add the thinking bubble if we're proceeding with normal AI processing
                var aiBubble = AddAiMessage("Thinking...💭 ");
                bool isSimple = IsSimpleQuery(question);
                // fallback to AI if uncertain
                if (!isSimple && question.Length < 25)
                {
                    isSimple = await IsSimpleQueryAI(question);
                }
                string context = "";
                if (!isSimple)
                {
                    context = await SearchVectors(question);
                }
                var textBlock = (System.Windows.Controls.RichTextBox)aiBubble.Child;
                SetUIMessage(textBlock, "⚡ Thinking deeper...🧠\n"); // clear "Thinking..."
                await CallOllamaStreaming(question, context, isSimple, textBlock);
            }
            catch (Exception ex)
            {
                AddAiMessage("⚠️ Error: " + ex.Message);
            }
            finally
            {
                _isProcessing = false;
                SetUiEnabled(true);
                QuestionBox.Focus();
            }
        }

        private void SetUiEnabled(bool isEnabled, string buttontext = "")
        {
            if (isEnabled)
            {
                SendButton.Content = "Send";
                SendButton.Width = 55; // auto
            }
            else
            {
                SendButton.Content = "🛑 Stop Thinking..💭";
                SendButton.Width = 120; // auto
            }
            if (!string.IsNullOrEmpty(buttontext))
            {
                SendButton.Content = buttontext;
            }
            QuestionBox.IsEnabled = isEnabled;
            SendButton.IsEnabled = true; // ✅ IMPORTANT: keep button enabled always
        }

        // Helper to replace all content in RichTextBox (like uiText.Text = ...)
        private void SetUIMessage(System.Windows.Controls.RichTextBox? uiText, string message)
        {
            if (uiText == null) return;
            uiText.Document.Blocks.Clear();
            uiText.Document.Blocks.Add(new Paragraph(new Run(message)));
        }

        // Helper to append content in RichTextBox (like uiText.Text += ...)
        private void AppendUIMessage(System.Windows.Controls.RichTextBox? uiText, string message)
        {
            if (uiText == null) return;
            var para = uiText.Document.Blocks.LastBlock as Paragraph;
            if (para == null)
            {
                para = new Paragraph();
                uiText.Document.Blocks.Add(para);
            }
            para.Inlines.Add(new Run(message + "\n"));
        }

        private bool IsSimpleQuery(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return true;

            q = q.ToLower().Trim();

            // greetings
            var simpleWords = new[]
            {
                "hi", "hello", "hey", "thanks", "thank you",
                "ok", "okay", "cool"
            };

            if (simpleWords.Contains(q))
                return true;

            // short + no technical keywords
            var techIndicators = new[]
            {
                "error", "exception", "api", "code", "method",
                "class", "sql", "json", "why", "how", "fix",
                "issue", "bug"
            };

            bool hasTech = techIndicators.Any(k => q.Contains(k));

            if (q.Length < 15 && !hasTech)
                return true;

            return false;
        }

        private async Task<bool> IsSimpleQueryAI(string question)
        {
            try
            {
                var prompt = $@"Classify the query. Respond with ONLY ONE WORD: GREETING or OTHER.

GREETING: hi, hello, hey, thanks, casual conversation
OTHER: technical questions, requests, anything else

Query: {question}

Response:";

                var req = new
                {
                    model = "phi3",
                    prompt = prompt,
                    stream = false,
                    options = new
                    {
                        temperature = 0.1,  // Very low for classification
                        top_p = 0.3,        // Reduce randomness
                        num_predict = 10    // Only expect 1-2 words
                    }
                };

                // Use a separate CancellationTokenSource for this operation
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    try
                    {
                        var res = await http.PostAsync(
                            "http://localhost:11434/api/generate",
                            new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json"),
                            cts.Token
                        );

                        if (!res.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"IsSimpleQueryAI - HTTP Error: {res.StatusCode}");
                            return false;
                        }

                        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

                        var output = json.RootElement.GetProperty("response")
                            .GetString()?.Trim().ToUpper();

                        return output == "GREETING";
                    }
                    catch (OperationCanceledException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"IsSimpleQueryAI - Request timeout (15s): {ex.Message}");
                        return false;
                    }
                    catch (HttpRequestException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"IsSimpleQueryAI - Connection error: {ex.Message}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IsSimpleQueryAI Error: {ex.Message}");
                return false;
            }
        }

        // BROWSE PROJECT FOLDER
        private void BrowseRepoPath(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                selectedpath.Text = dialog.SelectedPath;
                projectRoot = dialog.SelectedPath;
            }
        }

        // REFRESH PROJECT INDEX
        private async void OnRefreshProject(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(projectRoot))
            {
                AddAiMessage("Project directory not found.");
                return;
            }

            var aiBubble = AddAiMessage("Indexing project files...");
            var textBlock = (System.Windows.Controls.RichTextBox)aiBubble.Child;

            var vectors = await IndexRepository(textBlock);

            Directory.CreateDirectory(Path.GetDirectoryName(repoVectorPath)!);

            await File.WriteAllTextAsync(
                repoVectorPath,
                JsonSerializer.Serialize(vectors)
            );

            SetUIMessage(textBlock, $"Project index built. Chunks: {vectors.Count}");
        }

        // REFRESH KB INDEX
        private async void OnRefreshKB(object sender, RoutedEventArgs e)
        {
            var aiBubble = AddAiMessage("Indexing knowledge base...");
            var textBlock = (System.Windows.Controls.RichTextBox)aiBubble.Child;
            var vectors = await IndexKnowledgeBase(textBlock);

            Directory.CreateDirectory(Path.GetDirectoryName(kbVectorPath)!);

            await File.WriteAllTextAsync(
                kbVectorPath,
                JsonSerializer.Serialize(vectors)
            );

            SetUIMessage(textBlock, $"KB index built. Chunks: {vectors.Count}");
        }

        // INDEX REPOSITORY
        private async Task<List<VectorChunk>> IndexRepository(System.Windows.Controls.RichTextBox? uiText = null)
        {
            var result = new List<VectorChunk>();

            var excludedDirs = new[]
            {
                "\\bin\\", "\\obj\\", "\\.git\\", "\\node_modules\\",
                "\\dist\\", "\\build\\", "\\.vs\\"
            };

            var allowedExtensions = new[]
            {
                ".cs", ".ts", ".tsx", ".py", ".json", ".sql", ".cshtml", ".xaml", ".config"
            };

            var files = Directory.GetFiles(projectRoot, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                    allowedExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) &&
                    !excludedDirs.Any(dir => f.Contains(dir, StringComparison.OrdinalIgnoreCase))
                );
            var semaphore = new SemaphoreSlim(4); // 🔥 limit parallelism

            int remainingFilesCount = files.Count();
            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync();

                try
                {
                    remainingFilesCount--;
                    if (file.Contains(".min.") || file.EndsWith(".bundle.js"))
                    {
                        return;
                    }

                    var text = await File.ReadAllTextAsync(file);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    var fileHeader = $@"
                        File: {Path.GetFileName(file)}
                        Path: {file}

                        ----------------
                        ";

                    var chunks = ChunkText(fileHeader + text);

                    foreach (var chunk in chunks)
                    {
                        var enriched = chunk.ToLower();

                        // boost keywords (helps embeddings)
                        if (file.EndsWith(".cs"))
                            enriched = "csharp code " + enriched;

                        if (file.Contains("controller", StringComparison.OrdinalIgnoreCase))
                            enriched = "api controller " + enriched;

                        if (file.Contains("service", StringComparison.OrdinalIgnoreCase))
                            enriched = "business logic " + enriched;

                        var embedding = await GetEmbedding(enriched);

                        result.Add(new VectorChunk
                        {
                            Source = file,
                            Content = chunk,
                            Embedding = embedding
                        });

                        if (uiText != null)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke((Delegate)(() =>
                            {
                                var totalFiles = files.Count();
                                var processedFiles = totalFiles - remainingFilesCount;
                                var percent = (int)((processedFiles * 100.0) / totalFiles);
                                SetUIMessage(uiText,
                                    $"📂 Files: {processedFiles}/{totalFiles} ({percent}%)\n" +
                                    $"🧩 Chunks: {result.Count} processed\n" +
                                    $"⚙️ Processing: \n\n{file}");
                            }));
                        }
                    }
                }
                catch { }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);


            return result;
        }

        // INDEX KNOWLEDGE BASE WEBSITE
        private async Task<List<VectorChunk>> IndexKnowledgeBase(System.Windows.Controls.RichTextBox? uiText = null)
        {
            var result = new List<VectorChunk>();

            string startUrl = "https://support.znode.com/support/solutions";

            var visited = new HashSet<string>();
            var queue = new Queue<string>();

            var lockObj = new object();

            queue.Enqueue(startUrl);

            int workerCount = 4;
            var tasks = new List<Task>();

            for (int i = 0; i < workerCount; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while (true)
                    {
                        string? url = null;

                        lock (lockObj)
                        {
                            if (queue.Count > 0)
                            {
                                url = queue.Dequeue();
                            }
                        }

                        if (url == null)
                        {
                            await Task.Delay(200); // wait for new items

                            lock (lockObj)
                            {
                                if (queue.Count == 0)
                                    return; // exit worker safely
                            }

                            continue;
                        }

                        try
                        {
                            lock (lockObj)
                            {
                                if (visited.Contains(url))
                                    continue;

                                visited.Add(url);
                            }

                            var html = await http.GetStringAsync(url);

                            var doc = new HtmlDocument();
                            doc.LoadHtml(html);

                            var junkNodes = doc.DocumentNode.SelectNodes(
                                "//script|//style|//nav|//header|//footer|//aside"
                            );

                            if (junkNodes != null)
                            {
                                foreach (var node in junkNodes)
                                    node.Remove();
                            }

                            var body = doc.DocumentNode.SelectSingleNode("//body");

                            var text = body?.InnerText ?? "";

                            // CLEAN TEXT
                            text = Regex.Replace(text, @"Home.*?Search", "", RegexOptions.IgnoreCase);
                            text = Regex.Replace(text, @"TABLE OF CONTENTS.*?Introduction", "", RegexOptions.IgnoreCase);
                            text = Regex.Replace(text, @"Sign In|Sign Up|Toggle navigation", "", RegexOptions.IgnoreCase);
                            text = Regex.Replace(text, @"\s+", " ").Trim();
                            text = text.Replace("Sign in", "")
                                       .Replace("Submit a ticket", "")
                                       .Replace("Toggle navigation", "")
                                       .Trim();


                            var chunks = ChunkText(text);

                            var embedTasks = chunks.Select(async chunk =>
                            {
                                var embedding = await GetEmbedding(chunk);

                                lock (result)
                                {
                                    result.Add(new VectorChunk
                                    {
                                        Source = url,
                                        Content = chunk,
                                        Embedding = embedding
                                    });

                                    if (uiText != null)
                                    {
                                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            SetUIMessage(uiText, " chunks processed : " + result.Count + "\n source : " + url);
                                        });
                                    }
                                }
                            });

                            await Task.WhenAll(embedTasks);

                            var links = doc.DocumentNode.SelectNodes("//a[@href]");

                            if (links != null)
                            {
                                foreach (var link in links)
                                {
                                    var href = link.GetAttributeValue("href", "");
                                    if (string.IsNullOrWhiteSpace(href)) continue;

                                    if (href.StartsWith("/support"))
                                    {
                                        var full = "https://support.znode.com" + href;

                                        lock (lockObj)
                                        {
                                            if (!visited.Contains(full))
                                            {
                                                queue.Enqueue(full);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            return result;
        }

        // CHUNK TEXT
        private List<string> ChunkText(string text, int size = 1200)
        {
            var chunks = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return chunks;

            var sentences = text.Split(new[] { '.', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var current = new StringBuilder();

            foreach (var s in sentences)
            {
                var sentence = s.Trim();

                if (string.IsNullOrWhiteSpace(sentence))
                    continue;

                // 🔥 DO NOT SKIP SHORT SENTENCES
                // Instead, merge them into context

                if (current.Length + sentence.Length > size)
                {
                    if (current.Length > 0)
                    {
                        chunks.Add(current.ToString());
                        current.Clear();
                    }
                }

                current.Append(sentence + ". ");
            }

            if (current.Length > 0)
                chunks.Add(current.ToString());

            return chunks;
        }

        // GET EMBEDDING
        private async Task<float[]> GetEmbedding(string text)
        {
            var req = new
            {
                model = "nomic-embed-text",
                prompt = text.ToLower()
            };

            var res = await http.PostAsync(
                "http://localhost:11434/api/embeddings",
                new StringContent(
                    JsonSerializer.Serialize(req),
                    Encoding.UTF8,
                    "application/json")
            );

            var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

            return json.RootElement
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToArray();
        }

        // VECTOR SEARCH
        private async Task<string> SearchVectors(string question)
        {
            var allChunks = new List<VectorChunk>();

            if (File.Exists(repoVectorPath))
            {
                var repoJson = await File.ReadAllTextAsync(repoVectorPath);
                var repo = JsonSerializer.Deserialize<List<VectorChunk>>(repoJson);
                if (repo != null) allChunks.AddRange(repo);
            }

            if (File.Exists(kbVectorPath))
            {
                var kbJson = await File.ReadAllTextAsync(kbVectorPath);
                var kb = JsonSerializer.Deserialize<List<VectorChunk>>(kbJson);
                if (kb != null) allChunks.AddRange(kb);
            }

            var queryVec = await GetEmbedding(question);

            var ranked = allChunks
                .Select(v => new
                {
                    v.Content,
                    v.Source,
                    Score = CosineSimilarity(queryVec, v.Embedding)
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

        // COSINE SIMILARITY
        private double CosineSimilarity(float[] a, float[] b)
        {
            int len = Math.Min(a.Length, b.Length);

            double dot = 0;
            double magA = 0;
            double magB = 0;

            for (int i = 0; i < len; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB) + 1e-8);
        }

        // CALL OLLAMA
        private async Task<string> CallOllamaStreaming(string question, string context, bool isSimple, System.Windows.Controls.RichTextBox? uiText = null)
        {
            _isResponseGenerating = true;
            string prompt;
            string modelToUse = "phi3"; // ✅ fixed

            bool hasContext = !string.IsNullOrWhiteSpace(context);

            var defaultrequest = new
            {
                model = modelToUse,
                prompt = question,
                stream = true
            };
            var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(defaultrequest),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            // 🔥 Decide model smartly
            if (isSimple || !hasContext)
            {
                prompt = $@"
                User: {question}
                Assistant:";

                var reqWithoutContext = new
                {
                    model = modelToUse,
                    prompt = prompt,
                    stream = true
                };
                request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(reqWithoutContext),
                        Encoding.UTF8,
                        "application/json"
                    )
                };
            }
            else
            {
                // 🔥 Trim context to avoid slowdown
                //if (context.Length > 2000)
                //    context = context.Substring(0, 2000);

                prompt = $@"
                        Answer using ONLY the context.

                        If not found, say: I don't know.

                        Context:
                        {context}

                        Question:
                        {question}

                        Answer:";

                // 🔥 Use DeepSeek only if context is meaningful
                modelToUse = context.Length > 400 ? "deepseek-coder:6.7b" : "phi3";

                var reqWithContext = new
                {
                    model = modelToUse,
                    prompt = prompt,
                    stream = true,
                    options = new
                    {
                        temperature = 0.2,
                        num_predict = 500 // 🔥 reduced from 300
                    }
                };
                request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(reqWithContext),
                        Encoding.UTF8,
                        "application/json"
                    )
                };
            }


            try
            {
                _chatHistory.Add(new ChatMessage
                {
                    Role = "user",
                    Content = question
                });

                _currentCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));

                var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    _currentCts.Token
                );

                if (uiText != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        SetUIMessage(uiText, string.Empty); // ✅ show model being used
                    });
                }
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var result = new StringBuilder();

                int counter = 0;
                SetUiEnabled(false, "📝 Generating..."); // ✅ ensure UI is enabled during streaming
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(); // ✅ FIX 1 (remove WaitAsync)

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var json = JsonDocument.Parse(line);

                        if (json.RootElement.TryGetProperty("response", out var r))
                        {
                            var token = r.GetString();

                            if (string.IsNullOrEmpty(token))
                                continue;

                            result.Append(token);

                            counter++;

                            // 🔥 Throttle UI updates (VERY IMPORTANT)
                            if (uiText != null && counter % 2 == 0)
                            {
                                if (_isResponseGenerating)
                                {
                                    var textSnapshot = result.ToString();

                                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        SetUIMessage(uiText, textSnapshot);
                                    }, System.Windows.Threading.DispatcherPriority.Background);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // ignore bad chunks
                    }
                }
                var finalText = result.ToString();
                if (string.IsNullOrWhiteSpace(finalText))
                {
                    finalText = "⚠️ No answer generated. Try refining your question.";
                }
                if (uiText != null)
                {

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SetUIMessage(uiText, finalText); // ✅ show final answer
                    });
                }
                _chatHistory.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = finalText
                });

                SaveChatHistory();

            }
            catch (OperationCanceledException)
            {
                if (uiText != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        SetUIMessage(uiText, "⚠️ Request timed out. Try again or refine your question.");
                    });
                }
            }
            finally
            {
                _isResponseGenerating = false;
            }
            return string.Empty;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;   // ❌ prevent close
            this.Hide();       // 👈 hide instead
        }

        private void QuestionBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            textBox.Height = Double.NaN; // Reset to auto
            textBox.UpdateLayout();

            var formattedText = new FormattedText(
                textBox.Text + "\n", // Add a new line to ensure space for last line
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch),
                textBox.FontSize,
                Brushes.Black,
                new NumberSubstitution(),
                1);

            double desiredHeight = formattedText.Height + textBox.Padding.Top + textBox.Padding.Bottom + 10;
            textBox.Height = Math.Min(Math.Max(desiredHeight, textBox.MinHeight), textBox.MaxHeight);

        }
    }
}