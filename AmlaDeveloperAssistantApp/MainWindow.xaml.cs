using HtmlAgilityPack;
using AmlaDeveloperAssistantApp.Services;

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
        private string jiraToken = Constants.JiraAuthToken;
        private string jiraBaseUrl = Constants.JiraBaseUrl;
        private string jiraEmail = Constants.JiraUsername;

        // Jira services
        private JiraService? _jiraService;
        private FixSuggestionService? _fixSuggestionService;

        private AssistantAiService _aiService;

        // Jira configuration constants are now in Constants.cs

        private static readonly string JiraConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AmlaDeveloperAssistant",
            "jira_config.json"
        );
        private IntentDetectionService _intentDetectionService;
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

            // Initialize services
            try
            {
                _jiraService = new JiraService(Constants.JiraBaseUrl, Constants.JiraUsername, Constants.JiraAuthToken);
                _fixSuggestionService = new FixSuggestionService();
                _aiService = new AssistantAiService(
                    repoVectorPath,
                    kbVectorPath,
                    chatHistoryPath,
                    GetEmbedding,
                    CosineSimilarity
                );
                _intentDetectionService = new IntentDetectionService(GetEmbedding, CosineSimilarity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize services: {ex.Message}");
            }

            // position bottom right
            var area = Screen.PrimaryScreen.WorkingArea;
            Left = area.Width - Width - 10;
            Top = area.Height - Height - 10;

            // fire and forget (non-blocking)
            var bubble = AddAiMessage(Constants.LoadingTicket); // Reuse LoadingTicket for initialization
            var textBlock = (System.Windows.Controls.RichTextBox)bubble.Child;


            WarmUpModels(textBlock);

        }

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

                }
            }
            catch (Exception ex)
            {
            }
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
                    SetUIMessage(uiText, Constants.AnalyzingTicket); // Reuse AnalyzingTicket for warmup
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
                        UpdateUI(uiText, $"\n{Constants.LoadingTicket} {model}...");

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
                        UpdateUI(uiText, $"\n{Constants.LoadingTicket} {model}...");

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
                UpdateUI(uiText, Constants.Error + ex.Message);
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

            var paragraph = new Paragraph();
            var urlRegex = new Regex(@"(https?://[\w\-._~:/?#[\]@!$&'()*+,;=%]+)");
            int lastIndex = 0;
            foreach (Match match in urlRegex.Matches(text))
            {
                // Add text before the link
                if (match.Index > lastIndex)
                {
                    paragraph.Inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                }
                // Add the hyperlink
                var link = new Hyperlink(new Run(match.Value))
                {
                    NavigateUri = new Uri(match.Value)
                };
                link.RequestNavigate += (s, e) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                };
                paragraph.Inlines.Add(link);
                lastIndex = match.Index + match.Length;
            }
            // Add any remaining text
            if (lastIndex < text.Length)
            {
                paragraph.Inlines.Add(new Run(text.Substring(lastIndex)));
            }

            richTextBox.Document = new FlowDocument(paragraph)
            {
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent
            };

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

                // Only add the thinking bubble if we're proceeding with normal AI processing
                var aiBubble = AddAiMessage(Constants.Thinking);

                var questionVector = await _aiService.GetEmbedding(question);
                try
                {
                    // Check if this is a Jira ticket key
                    if(await HandleJiraTicketInput(question, questionVector))
                    {
                        return;
                    }

                }
                catch (Exception ex)
                {
                    AddAiMessage(Constants.JiraIntentError + ex.Message);
                }

                // Check for open sphere intent using AI
                if (await _intentDetectionService.IsOpenSphereIntentVectorSearch(questionVector))
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

                bool isSimple = IsSimpleQuery(question);
                // fallback to AI if uncertain
                if (!isSimple && question.Length < 25)
                {
                    isSimple = await _intentDetectionService.IsSimpleQueryVectorSearch(questionVector);
                }
                string context = "";
                if (!isSimple)
                {
                    context = await _aiService.SearchVectors(question, questionVector);
                }
                var textBlock = (System.Windows.Controls.RichTextBox)aiBubble.Child;
                SetUIMessage(textBlock, Constants.ThinkingDeeper); // clear "Thinking..."
                await CallOllamaChatStreaming(question, context, isSimple, textBlock);
            }
            catch (Exception ex)
            {
                AddAiMessage(Constants.Error + ex.Message);
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
                "issue", "bug","znode","sphere","artify","nelli",
                "troubleshoot","problem","debug","crash","fail","stacktrace",
                "ecommerce","10x","webstore","headless","commerce",
                "commerceportal","admin",

            };

            bool hasTech = techIndicators.Any(k => q.Contains(k));

            if (q.Length < 15 && !hasTech)
                return true;

            return false;
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
                AddAiMessage(Constants.ProjectDirectoryNotFound);
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

            SetUIMessage(textBlock, string.Format(Constants.ProjectIndexBuilt, vectors.Count));
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

            SetUIMessage(textBlock, string.Format(Constants.KBIndexBuilt, vectors.Count));
        }

        // INDEX REPOSITORY (delegated to service)
        private async Task<List<AssistantAiService.VectorChunk>> IndexRepository(System.Windows.Controls.RichTextBox? uiText = null)
        {
            // Call the service for indexing logic
            return await _aiService.IndexRepository(projectRoot, uiText, msg => SetUIMessage(uiText, msg));
        }

        // INDEX KNOWLEDGE BASE WEBSITE (delegated to service)
        private async Task<List<AssistantAiService.VectorChunk>> IndexKnowledgeBase(System.Windows.Controls.RichTextBox? uiText = null)
        {
            // Call the service for indexing logic
            return await _aiService.IndexKnowledgeBase(uiText, msg => SetUIMessage(uiText, msg));
        }

        // CHUNK TEXT

         
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

        // VECTOR SEARCH

        // VECTOR SEARCH


        // VECTOR SEARCH


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
                        Znode is an ecommerce platform.
                        
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
                SetUIMessage(uiText, string.Empty); // show model being used
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
                    finalText = Constants.NoAnswerGenerated;
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
                        SetUIMessage(uiText, Constants.RequestTimedOut);
                    });
                }
            }
            finally
            {
                _isResponseGenerating = false;
            }
            return string.Empty;
        }

        private async Task<string> CallOllamaChatStreaming(
            string question,
            string context,
            bool isSimple,
            System.Windows.Controls.RichTextBox? uiText = null)
        {
            _isResponseGenerating = true;

            string modelToUse = "phi3";
            bool hasContext = !string.IsNullOrWhiteSpace(context);

            try
            {
                // 🧠 Decide model
                if (!isSimple && hasContext && context.Length > 400)
                    modelToUse = "deepseek-coder:6.7b";

                // 🧠 Build messages
                var messages = new List<object>();

                // 🔥 SYSTEM MESSAGE
                if (isSimple || !hasContext)
                {
                    messages.Add(new
                    {
                        role = "system",
                        content = "You are a friendly assistant. Keep answers short and natural."
                    });
                }
                else
                {
                    messages.Add(new
                    {
                        role = "system",
                        content = @"Answer ONLY using the provided context.
Znode is an ecommerce platform.
If answer is not found, say: I don't know."
                    });
                }

                // 🔥 Add limited history (IMPORTANT)
                foreach (var msg in _chatHistory.TakeLast(6))
                {
                    messages.Add(new
                    {
                        role = msg.Role,
                        content = msg.Content
                    });
                }

                // 🔥 Current question
                if (hasContext && !isSimple)
                {
                    messages.Add(new
                    {
                        role = "user",
                        content = $"Context:\n{context}\n\nQuestion:\n{question}"
                    });
                }
                else
                {
                    messages.Add(new
                    {
                        role = "user",
                        content = question
                    });
                }

                var requestBody = new
                {
                    model = modelToUse,
                    messages = messages,
                    stream = true,
                    options = new
                    {
                        temperature = 0.2,
                        num_predict = 300
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/chat")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(requestBody),
                        Encoding.UTF8,
                        "application/json")
                };

                // 🧾 Save user message
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
                        SetUIMessage(uiText, string.Empty);
                    });
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var result = new StringBuilder();
                int counter = 0;

                SetUiEnabled(false, "📝 Generating...");

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var json = JsonDocument.Parse(line);

                        // 🔥 NEW PARSING FOR /api/chat
                        if (json.RootElement.TryGetProperty("message", out var msgObj) &&
                            msgObj.TryGetProperty("content", out var content))
                        {
                            var token = content.GetString();

                            if (string.IsNullOrEmpty(token))
                                continue;

                            result.Append(token);
                            counter++;

                            // 🔥 Streaming UI (throttled)
                            if (uiText != null && counter % 2 == 0)
                            {
                                if (_isResponseGenerating)
                                {
                                    var snapshot = result.ToString();

                                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        SetUIMessage(uiText, snapshot);
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
                        // ignore malformed chunk
                    }
                }

                var finalText = result.ToString();

                if (string.IsNullOrWhiteSpace(finalText))
                    finalText = Constants.NoAnswerGenerated;

                if (uiText != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SetUIMessage(uiText, finalText);
                    });
                }

                // 🧾 Save assistant response
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
                        SetUIMessage(uiText, Constants.RequestTimedOut);
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


        // Use JiraService for all JIRA ticket handling and UI integration
        private async Task<bool> HandleJiraTicketInput(string input, float[] questionVector)
        {
            if (_jiraService == null)
                return false;

            // Fast path: regex match for common Jira ticket patterns (e.g., PROJ-123)
            if (_intentDetectionService.IsJiraTicketIntentRegx(input))
            {
                await ShowJiraTicketUI(input.ToUpper().Trim());
                return true;
            }

            // AI intent detection
            var isJiraIntent = await _intentDetectionService.IsJiraTicketIntentVectorSearch(questionVector);
            if (!isJiraIntent)
                return false;

            var extractedTicketId = await _aiService.ExtractJiraTicketIdAI(input);
            if (!string.IsNullOrEmpty(extractedTicketId))
            {
                await ShowJiraTicketUI(extractedTicketId);
                return true;
            }
            else
            {
                AddAiMessage("❌ I detected a Jira request, but couldn't extract a valid ticket ID.\n\nTry:\n• 'Show PROJ-123'\n• 'Open BUG-456'\n• 'Get FEAT-789'");
                return false;
            }
        }


        // Show Jira ticket in UI using JiraService
        private async Task ShowJiraTicketUI(string ticketKey)
        {
            try
            {
                AddAiMessage($"🎫 Fetching Jira ticket: {ticketKey}");
                var bubble = AddAiMessage("⏳ Loading ticket...");
                var textBlock = (System.Windows.Controls.RichTextBox)bubble.Child;
                var ticket = await _jiraService.GetTicketAsync(ticketKey);
                DisplayJiraTicketWithLink(textBlock, ticket);
                AddJiraFixSuggestionButton(ticket);
            }
            catch (Exception ex)
            {
                AddAiMessage($"❌ Error fetching ticket: {ex.Message}");
            }
        }

        // Display Jira ticket with clickable link
        private void DisplayJiraTicketWithLink(System.Windows.Controls.RichTextBox rtb, JiraTicket ticket)
        {
            rtb.Document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run($"🎫 JIRA TICKET: {ticket.Key}\n\n"));
            paragraph.Inlines.Add(new Run($"📋 Summary: {ticket.Summary}\n"));
            var link = new Hyperlink(new Run("🔗 Open in Browser"))
            {
                NavigateUri = new Uri(ticket.BrowserUrl)
            };
            link.RequestNavigate += (s, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            };
            paragraph.Inlines.Add(link);
            paragraph.Inlines.Add(new Run($"\n📊 Status: {ticket.Status}"));
            paragraph.Inlines.Add(new Run($"\n⚡ Priority: {ticket.Priority}"));
            paragraph.Inlines.Add(new Run($"\n🏷️ Type: {ticket.IssueType}\n\n"));
            paragraph.Inlines.Add(new Run($"📝 Description:\n{ticket.Description}"));
            rtb.Document.Blocks.Add(paragraph);
        }

        // Add fix suggestion button for Jira ticket
        private void AddJiraFixSuggestionButton(JiraTicket ticket)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = $"💡 Get Fix Suggestions for {ticket.Key}",
                Background = Brushes.DarkGreen,
                Foreground = Brushes.White,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(5),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += async (s, e) =>
            {
                _isProcessing = true;
                SetUiEnabled(false);
                try
                {
                    var bubble = AddAiMessage("🧠 Analyzing ticket with AI...\n⏳ This may take a moment...");
                    var textBlock = (System.Windows.Controls.RichTextBox)bubble.Child;
                    var suggestion = await _fixSuggestionService.GetFixSuggestionsForTicket(ticket, _aiService, projectRoot);
                    if (suggestion.IsSuccess)
                    {
                        string formatedSuggestion = _fixSuggestionService.FormatFixSuggestion(suggestion);
                        SetUIMessage(textBlock, formatedSuggestion);

                        // 🧾 Save assistant response
                        _chatHistory.Add(new ChatMessage
                        {
                            Role = "user",
                            Content = $"How to fix Jira ticket {ticket.Key} : {ticket.Summary} ?"
                        });
                        _chatHistory.Add(new ChatMessage
                        {
                            Role = "assistant",
                            Content = formatedSuggestion
                        });
                        SaveChatHistory();
                    }
                    else
                    {
                        SetUIMessage(textBlock, $"❌ {suggestion.Error}");
                    }
                }
                catch (Exception ex)
                {
                    AddAiMessage($"❌ Error getting suggestions: {ex.Message}");
                }
                finally
                {
                    _isProcessing = false;
                    SetUiEnabled(true);
                }
            };
            var container = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(10),
                Margin = new Thickness(5, 5, 60, 5),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Child = button
            };
            ChatPanel.Children.Add(container);
            ChatScroll.ScrollToEnd();
        }

        // NEW: Get fix suggestions from Ollama
        private async Task GetFixSuggestionsForTicket(JiraTicket ticket)
        {
            if (_fixSuggestionService == null || string.IsNullOrWhiteSpace(ticket.Description))
            {
                AddAiMessage("❌ Cannot analyze ticket");
                return;
            }

            _isProcessing = true;
            SetUiEnabled(false);

            try
            {
                var bubble = AddAiMessage("🧠 Analyzing ticket with AI...\n⏳ This may take a moment...");
                var textBlock = (System.Windows.Controls.RichTextBox)bubble.Child;

                var projectContext = await BuildProjectContext();

                string kbcontext = "";
                string projcontext = "";
                string historycontext = "";
                try
                {
                    kbcontext = await _aiService.SearchKBVectors(ticket.Description);
                    projcontext = await _aiService.SearchRepoVectors(ticket.Description);
                    //historycontext = await SearchHistoryVectors(ticket.Description, 500);
                }
                catch { }
                var suggestion = await _fixSuggestionService.AnalyzeTicketAsync(
                    ticket.Key,
                    $"{ticket.Description}\n INFORMATION CONTEXT:{kbcontext}\n",// HISTORY CONTEXT:{historycontext}\n",
                    $"{projectContext}{projcontext}"
                );

                if (suggestion.IsSuccess)
                {
                    DisplayFixSuggestion(textBlock, suggestion);
                }
                else
                {
                    SetUIMessage(textBlock, $"❌ {suggestion.Error}");
                }
            }
            catch (Exception ex)
            {
                AddAiMessage($"❌ Error getting suggestions: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                SetUiEnabled(true);
            }
        }

        // NEW: Display fix suggestions in formatted way
        private void DisplayFixSuggestion(System.Windows.Controls.RichTextBox rtb, FixSuggestion suggestion)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"✅ FIX SUGGESTIONS FOR {suggestion.TicketKey}\n");
            sb.AppendLine("═══════════════════════════════════════\n");

            if (!string.IsNullOrWhiteSpace(suggestion.IssueSummary))
                sb.AppendLine($"📌 Issue Summary:\n{suggestion.IssueSummary}\n");

            if (!string.IsNullOrWhiteSpace(suggestion.FixType))
                sb.AppendLine($"🔧 Fix Type: {suggestion.FixType}\n");

            if (!string.IsNullOrWhiteSpace(suggestion.AffectedAreas))
                sb.AppendLine($"📍 Affected Areas:\n{suggestion.AffectedAreas}\n");

            if (!string.IsNullOrWhiteSpace(suggestion.SuggestedFiles))
                sb.AppendLine($"📁 Suggested Files:\n{suggestion.SuggestedFiles}\n");

            if (!string.IsNullOrWhiteSpace(suggestion.MethodsToCheck))
                sb.AppendLine($"⚙️ Methods to Check:\n{suggestion.MethodsToCheck}\n");

            if (!string.IsNullOrWhiteSpace(suggestion.PriorityAreas))
                sb.AppendLine($"🎯 Priority Areas:\n{suggestion.PriorityAreas}\n");

            if (!string.IsNullOrWhiteSpace(suggestion.SuggestedApproach))
                sb.AppendLine($"💡 Suggested Approach:\n{suggestion.SuggestedApproach}\n");

            SetUIMessage(rtb, sb.ToString());
        }

        // NEW: Build project context for better analysis
        private async Task<string> BuildProjectContext()
        {
            var context = new StringBuilder();

            try
            {
                if (Directory.Exists(projectRoot))
                {
                    context.AppendLine($"Project Root: {projectRoot}");

                    var projectFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
                    if (projectFiles.Length > 0)
                    {
                        context.AppendLine($"Found {projectFiles.Length} C# projects");
                    }

                    var srcDir = Path.Combine(projectRoot, "src");
                    if (Directory.Exists(srcDir))
                    {
                        var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
                        context.AppendLine($"Source files: {csFiles.Length}");
                    }
                }
            }
            catch { }

            return context.ToString();
        }

        // AI-based intent detection for Jira ticket input
        private async Task<bool> IsJiraTicketIntentAI(string question)
        {
            try
            {
                var prompt = $@"
                Classify the user query intent.

                Return ONLY one word:
                JIRA or OTHER

                JIRA includes:
                - open jira ticket
                - show jira issue
                - fetch ticket
                - get jira ticket
                - jira ticket PROJ-123
                - any intent to open/show/get a jira ticket or issue
                - mention of ticket keys like PROJ-123, BUG-456, FEAT-789

                OTHER includes:
                - general questions
                - code questions
                - anything not related to jira

                Query:
                {question}
                ";

                var req = new
                {
                    model = "phi3",
                    prompt = prompt,
                    stream = false
                };

                _currentCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                var res = await http.PostAsync(
                    "http://localhost:11434/api/generate",
                    new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json"),
                    _currentCts.Token
                );

                var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

                var output = json.RootElement.GetProperty("response")
                    .GetString()?.Trim().ToUpper();

                return output == "JIRA";
            }
            catch
            {
                return false;
            }
        }

        // Vector-based intent detection for Jira ticket input


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