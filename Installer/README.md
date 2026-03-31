# Installer Project for AmlaDeveloperAssistant

This folder contains a WiX Toolset installer project for packaging the AmlaDeveloperAssistant app, installing Ollama, and pulling required models.

## Prerequisites

- **WiX Toolset**: Download and install from https://wixtoolset.org/
- **WiX Toolset Visual Studio Extension**: For integration with Visual Studio (recommended)
- **Ollama Windows Installer**: Download from https://ollama.com/download and place the installer (e.g., `ollama-windows.exe`) in this folder.

## How to Use This Installer Project

1. **Build Your App**
   - In Visual Studio, build the `AmlaDeveloperAssistantApp` project in Release mode.
   - The output will be in `AmlaDeveloperAssistantApp/bin/Release/net10.0-windows/`.

2. **Copy Files**
   - Copy all files from the above output folder into the `Installer` folder (or reference them in the WiX script).

3. **Add Ollama Installer**
   - Place your downloaded Ollama Windows installer in this folder as `OllamaSetup.exe`.
   - The installer will automatically run this file during installation.

4. **Model Pull Script**
   - Use the provided `pull_ollama_models.ps1` script to automate model downloads after Ollama is installed.

5. **Edit Product.wxs**
   - Update `Product.wxs` to include your app files and the Ollama installer as components.
   - Add a custom action to run the PowerShell script after installation.
   - See comments in `Product.wxs` for where to add these items.

6. **Build the Installer**
   - In Visual Studio, right-click the Installer project and choose Build.
   - The output will be an `.msi` installer in the Installer/bin/Release/ folder.

7. **Run the Installer**
   - Double-click the `.msi` to install your app, Ollama, and required models.

## Example: Model Pull Script

A sample PowerShell script (`pull_ollama_models.ps1`) is provided to automate model downloads:

```powershell
# pull_ollama_models.ps1
ollama pull phi3
ollama pull deepseek-coder:6.7b
ollama pull nomic-embed-text
```

## References
- [WiX Toolset Documentation](https://wixtoolset.org/documentation/)
- [WiX Custom Actions](https://wixtoolset.org/documentation/manual/v3/customactions/)
- [How to: Create a WiX installer in Visual Studio](https://learn.microsoft.com/en-us/visualstudio/deployment/installer-projects-walkthrough?view=vs-2022)

---

If you are new to installers, follow the steps above and use the comments in `Product.wxs` as a guide. You can ask for help or clarification at any step!
