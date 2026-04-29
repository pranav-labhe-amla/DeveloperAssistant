using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

class Program
{
    [STAThread]
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("MCP Installer\n");


        // -----------------------------
        // STEP 0: TOKEN INPUT
        // -----------------------------
        Console.WriteLine("\n Enter tokens (type or right-click to paste):");

        Console.Write("Jira Token:  ");
        string jiraToken = ReadMasked();

        Console.Write("Git Token:   ");
        string gitToken = ReadMasked();

        Console.Write("New MCP Password:        ");
        string mcpToken = ReadMasked();

        Console.Write("Confirm MCP Password:");
        string mcpTokenConfirm = ReadMasked();

        if (mcpToken != mcpTokenConfirm)
        {
            Console.WriteLine("\n MCP passwords do not match. Aborting.");

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey(intercept: true);

            return 1;
        }

        // -----------------------------
        // STEP 1: Scope Selection
        // -----------------------------
        Console.WriteLine("Installing the MCP tool and custom github copilot agent.");
        bool isGlobal = true;

        Console.WriteLine(isGlobal
            ? "\n Using GLOBAL scope\n"
            : "\n Using WORKSPACE scope\n");

        // -----------------------------
        // STEP 2: Resolve Paths
        // -----------------------------
        var cwd = Directory.GetCurrentDirectory();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Each entry is (mcpPath, agentPath, label)
        List<(string mcpPath, string agentPath, string label)> targets = isGlobal
            ? [
                (
                    Path.Combine(appData, "Code", "User", "mcp.json"),
                    Path.Combine(userProfile, ".copilot", "agents", "Amlifi.agent.md"),
                    "VS Code (global)"
                ),
                (
                    Path.Combine(userProfile, ".mcp.json"),              // Visual Studio: %USERPROFILE%\.mcp.json
                    Path.Combine(userProfile, ".github", "agents", "Amlifi.agent.md"),
                    "Visual Studio (global)"
                )
              ]
            : [
                (
                    Path.Combine(cwd, ".vscode", "mcp.json"),
                    Path.Combine(cwd, ".github", "agents", "Amlifi.agent.md"),
                    "Workspace"
                )
              ];


        foreach (var (mcpPath, agentPath, label) in targets)
        {
            string agentContent = """
            ---
            name: Amlifi
            description: Assistant of the Amla employee. can help with Jira tickets, and knowledge base queries
            tools: ['amlifi/askAgent']
            ---
            ## Core Instruction

            - For EVERY user query:
              - Use the `amlifi/askAgent` tool with the user's input as the `query` parameter
              - Example: `amlifi/askAgent({ "query": "<user_input>" })`
            - Use the tool response as context
            - Use response or expanded response as context
            - If it is a bug or error:
              - Clearly suggest a fix and provide the code snippet in markdown format
            - If you are mentioning Frontend or Backend:
              - Specify exact file and method you are referring to
            - If you are mentioning code:
              - Provide the code snippet in markdown format
            - Be specific and detailed
            - If you are suggesting an approach:
              - Try to implement it and provide the code snippet in markdown format
            - If you have specific methods in your knowledge base:
              - Use them and provide examples
            - Suggest changes to the code if applicable
            - Provide alternative solutions if applicable
            - Do not imagine or make up information that is not in the response
            - Mention estimated time to complete the task if applicable for new employees
            """;
            if(label == "Visual Studio (global)")
            {
                agentContent = """
                ---
                name: Amlifi
                description: Assistant of the Amla employee. can help with Jira tickets, and knowledge base queries
                tools: 
                    - 'amlifi/askAgent'
                ---
                ## Core Instruction
                - For EVERY user query:
                  - Use the `amlifi/askAgent` tool with the user's input as the `query` parameter
                  - Example: `amlifi/askAgent({ "query": "<user_input>" })`
                - Use the tool response as context
                - Use response or expanded response as context
                - If it is a bug or error:
                  - Clearly suggest a fix and provide the code snippet in markdown format
                - If you are mentioning Frontend or Backend:
                  - Specify exact file and method you are referring to
                - If you are mentioning code:
                  - Provide the code snippet in markdown format
                - Be specific and detailed
                - If you are suggesting an approach:
                  - Try to implement it and provide the code snippet in markdown format
                - If you have specific methods in your knowledge base:
                  - Use them and provide examples
                - Suggest changes to the code if applicable
                - Provide alternative solutions if applicable
                - Do not imagine or make up information that is not in the response
                """;
            }
            Console.WriteLine($"\n [{label}]");

            Directory.CreateDirectory(Path.GetDirectoryName(mcpPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(agentPath)!);

            // -----------------------------
            // STEP 3: MCP CONFIG
            // -----------------------------
            Console.WriteLine(" Configuring MCP...");

            JsonObject root;

            if (File.Exists(mcpPath))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(mcpPath);
                    root = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
                    Console.WriteLine($" Loaded existing MCP config ? {mcpPath}");
                }
                catch
                {
                    Console.WriteLine(" Invalid JSON, recreating...");
                    root = new JsonObject();
                }
            }
            else
            {
                Console.WriteLine(" Creating new MCP config");
                root = new JsonObject();
            }

            if (root["servers"] is not JsonObject servers)
            {
                servers = new JsonObject();
                root["servers"] = servers;
            }

            const string serverName = "Amlifi";

            if (servers[serverName] == null)
            {
                servers[serverName] = new JsonObject
                {
                    ["url"] = "http://ollama-test.amla.io:5216/api/mcp",
                    ["type"] = "http",
                    ["headers"] = new JsonObject
                    {
                        ["Authorization"] = "Bearer ${env:AMLIFI_MCP_TOKEN}",
                        ["X-Computername"] = "${env:COMPUTERNAME}",
                        ["X-Username"] = "${env:USERNAME}"
                    }
                };
                if(label == "Visual Studio (global)")
                {
                    ((JsonObject)servers[serverName])["headers"] = new JsonObject
                    {
                        ["Authorization"] = $"Bearer {mcpToken}",
                        ["X-Computername"] = $"{Environment.GetEnvironmentVariable("COMPUTERNAME")}",
                        ["X-Username"] = $"{Environment.GetEnvironmentVariable("USERNAME")}"
                    };
                }
                Console.WriteLine($" MCP server added ? {mcpPath}");
            }
            else
            {
                Console.WriteLine($" MCP server already exists ? {mcpPath}, skipping");
            }

            if (File.Exists(mcpPath))
            {
                File.Copy(mcpPath, mcpPath + ".bak", overwrite: true);
            }

            await File.WriteAllTextAsync(mcpPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, IndentSize = 2 }));

            Console.WriteLine($" MCP config saved ? {mcpPath}");

            // -----------------------------
            // STEP 4: AGENT SETUP
            // -----------------------------
            Console.WriteLine(" Configuring agent...");

            bool agentExists = File.Exists(agentPath);
            await File.WriteAllTextAsync(agentPath, agentContent);
            Console.WriteLine(agentExists
                ? $" Agent file updated ? {agentPath}"
                : $" Agent file created ? {agentPath}");
        }

        // -----------------------------
        // STEP 6: REGISTER TOKENS
        // -----------------------------
        Console.WriteLine("\n Registering tokens...");

        try
        {
            using var httpClient = new HttpClient();

            var computerName = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? Environment.MachineName;
            var username = Environment.GetEnvironmentVariable("USERNAME") ?? Environment.UserName;

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", mcpToken);
            httpClient.DefaultRequestHeaders.Add("X-Computername", computerName);
            httpClient.DefaultRequestHeaders.Add("X-Username", username);
            httpClient.DefaultRequestHeaders.Add("X-Jira-Token", jiraToken);
            httpClient.DefaultRequestHeaders.Add("X-Git-Token", gitToken);

            var response = await httpClient.GetAsync("http://ollama-test.amla.io:5216/api/agent/setup");

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine(" Tokens registered successfully");
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($" Registration failed: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"   {body}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($" API call failed: {ex.Message}");
        }

        // -----------------------------
        // DONE
        // -----------------------------

        // Persist MCP token as a User-scoped environment variable so VS Code
        // can resolve ${env:AMLIFI_MCP_TOKEN} in mcp.json without manual setup.
        try
        {
            Environment.SetEnvironmentVariable("AMLIFI_MCP_TOKEN", mcpToken, EnvironmentVariableTarget.User);
            Console.WriteLine("\n AMLIFI_MCP_TOKEN environment variable set for current user");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n Could not set AMLIFI_MCP_TOKEN: {ex.Message}");
            Console.WriteLine("  Set it manually: [System.Environment]::SetEnvironmentVariable('AMLIFI_MCP_TOKEN','<token>','User')");
        }

        Console.WriteLine("\n Setup complete!");
        Console.WriteLine("\n Next steps:");
        Console.WriteLine("  1. Restart VS Code / Visual Studio (or system) so it picks up the new environment variable");
        Console.WriteLine("  2. Open GitHub Copilot Chat in either IDE");
        Console.WriteLine("  3. Switch to Agent mode and select @Amlifi");

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey(intercept: true);

        return 0;
    }

    // -----------------------------
    // Helper: Masked input (supports paste)
    // -----------------------------
    static string ReadMasked()
    {
        var input = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
                break;

            if (key.Key == ConsoleKey.Backspace && input.Length > 0)
            {
                input.Remove(input.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (key.Key == ConsoleKey.V &&
                     key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                // Ctrl+V: read clipboard via Windows Forms
                var pasted = System.Windows.Forms.Clipboard.GetText();
                foreach (var c in pasted)
                {
                    if (!char.IsControl(c))
                    {
                        input.Append(c);
                        Console.Write('*');
                    }
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                input.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        Console.WriteLine();
        return input.ToString();
    }
}
