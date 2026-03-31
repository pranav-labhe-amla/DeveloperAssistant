# AmlaDeveloperAssistant

AmlaDeveloperAssistant is a Windows desktop application (WPF, .NET 10) that acts as an AI-powered coding assistant for developers. It leverages local LLMs (such as Ollama models) and vector search to answer questions about your codebase and external knowledge sources.

## Features

- **Chat Interface:** Ask questions and get AI-powered answers in a chat-like UI.
- **Project Indexing:** Indexes your codebase for semantic search and context-aware answers.
- **Knowledge Base Indexing:** Crawls and indexes external documentation (e.g., Znode support site) for enhanced responses.
- **Local LLM Integration:** Connects to local Ollama models (e.g., phi3, deepseek-coder) for fast, private inference.
- **Contextual Answers:** Uses vector search to provide relevant code/documentation context to the AI.
- **Persistent Chat History:** Remembers your previous conversations.
- **Modern, Minimal UI:** Clean WPF interface with dark theme.

## Prerequisites

- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/) running locally with supported models (`phi3`, `deepseek-coder:6.7b`, `nomic-embed-text`)
	- **Install Ollama:** [https://ollama.com/download](https://ollama.com/download)
	- **Pull required models:**
		```sh
		ollama pull phi3
		ollama pull deepseek-coder:6.7b
		ollama pull nomic-embed-text
		```
- Internet connection (for knowledge base crawling)


## Getting Started


### Option 1: Using the Installer (Recommended)

1. **Build the Installer Project:**
	- Open the solution in Visual Studio 2022 or later.
	- Build the `Installer` project (WiX Toolset required).
	- The `Installer` folder now contains:
	  - A sample WiX installer script (`Product.wxs`) with clear comments and placeholders.
	  - A PowerShell script (`pull_ollama_models.ps1`) to automate model downloads.
	  - A step-by-step beginner guide in `Installer/README.md`.

2. **Run the Installer:**
	- The generated MSI will install AmlaDeveloperAssistant, Ollama, and pull required models automatically.
	- Follow on-screen instructions.

See `Installer/README.md` for a full, beginner-friendly walkthrough and details on adding your files.

### Option 2: Manual Setup

1. **Clone the repository:**
	```
	git clone <your-repo-url>
	```

2. **Open the solution:**
	- Open `AmlaDeveloperAssistantApp.sln` in Visual Studio 2022 or later.

3. **Restore and build:**
	- Restore NuGet packages and build the solution.

4. **Run Ollama:**
	- Make sure Ollama is running locally and the required models are pulled.

5. **Launch the app:**
	- Run the application from Visual Studio or by executing the built `.exe` in `bin/Debug/net10.0-windows/`.

## Usage

- **Select Project Folder:** Use the "Browse" button to select your codebase folder.
- **Load Files:** Click "Load Files" to index your project for semantic search.
- **Load KB:** Click "Load KB" to crawl and index the external knowledge base.
- **Ask Questions:** Type your question and press "Send" or Enter. The AI will answer using indexed code and documentation.

## Customization

- **Models:** You can change the models used by editing the code in `MainWindow.xaml.cs`.
- **Knowledge Base:** The default KB is set to Znode support, but you can modify the URL in the code.

## Technologies Used

- WPF (.NET 10)
- Ollama (local LLM inference)
- HtmlAgilityPack (web crawling)
- System.Text.Json (serialization)
- C#

## License

MIT License

