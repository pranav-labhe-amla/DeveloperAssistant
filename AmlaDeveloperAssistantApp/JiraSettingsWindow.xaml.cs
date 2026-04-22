using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using AmlaDeveloperAssistantApp.Services;

namespace AmlaDeveloperAssistantApp
{
    /// <summary>
    /// Interaction logic for JiraSettingsWindow.xaml
    /// </summary>
    public partial class JiraSettingsWindow : Window
    {
        private JiraConfiguration? _config;
        private readonly string _configPath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            "AmlaDeveloperAssistant",
            "jira_config.json");

        public JiraSettingsWindow()
        {
            InitializeComponent();
            LoadConfig();
        }

        private void LoadConfig()
        {
            _config = JiraConfiguration.Load(_configPath);
            if (_config != null)
            {
                BaseUrlTextBox.Text = _config.BaseUrl;
                UsernameTextBox.Text = _config.Username;
                AuthTokenTextBox.Password = _config.AuthToken;
                OllamaUrlTextBox.Text = _config.OllamaBaseUrl;
            }
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_config == null)
                _config = new JiraConfiguration();
            _config.BaseUrl = BaseUrlTextBox.Text;
            _config.Username = UsernameTextBox.Text;
            _config.AuthToken = AuthTokenTextBox.Password;
            _config.OllamaBaseUrl = OllamaUrlTextBox.Text;
            _config.Save(_configPath);
            System.Windows.MessageBox.Show("Jira configuration saved.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            _config = new JiraConfiguration();
            BaseUrlTextBox.Text = _config.BaseUrl;
            UsernameTextBox.Text = _config.Username;
            AuthTokenTextBox.Password = _config.AuthToken;
            OllamaUrlTextBox.Text = _config.OllamaBaseUrl;
        }

        // Add missing event handlers for XAML
        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Test Connection not implemented.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetDefaults_Click(sender, e);
        }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig_Click(sender, e);
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Cancel_Click(sender, e);
        }
    }
}
