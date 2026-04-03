using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WPFMessageBox = System.Windows.MessageBox;

namespace AmlaDeveloperAssistantApp
{
    /// <summary>
    /// Interaction logic for JiraSettingsWindow.xaml
    /// </summary>
    public partial class JiraSettingsWindow : Window
    {
        private Services.JiraConfiguration? _config;

        public JiraSettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                _config = Services.JiraConfiguration.Load();

                if (_config != null)
                {
                    BaseUrlTextBox.Text = _config.BaseUrl;
                    UsernameTextBox.Text = _config.Username;
                    AuthTokenTextBox.Password = _config.AuthToken;
                    OllamaUrlTextBox.Text = _config.OllamaBaseUrl;
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"Error loading settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_config == null)
                    _config = new Services.JiraConfiguration();

                _config.BaseUrl = BaseUrlTextBox.Text;
                _config.Username = UsernameTextBox.Text;
                _config.AuthToken = AuthTokenTextBox.Password;
                _config.OllamaBaseUrl = OllamaUrlTextBox.Text;

                _config.Save();

                WPFMessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            TestJiraConnection();
        }

        private async void TestJiraConnection()
        {
            try
            {
                var testService = new Services.JiraService(
                    BaseUrlTextBox.Text,
                    UsernameTextBox.Text,
                    AuthTokenTextBox.Password
                );

                // Try to fetch current user as a connection test
                var ticket = await testService.GetTicketAsync("invalid");
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("401") || ex.Message.Contains("403"))
                {
                    WPFMessageBox.Show("Authentication failed. Please check your credentials.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (ex.Message.Contains("404"))
                {
                    WPFMessageBox.Show("Ticket not found, but connection to Jira is working!", "Connection OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    WPFMessageBox.Show($"Connection test result: {ex.Message}", "Test Result", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (WPFMessageBox.Show("Reset all settings to defaults?", "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _config = new Services.JiraConfiguration();
                BaseUrlTextBox.Text = "https://amla.atlassian.net";
                UsernameTextBox.Text = "riya.agrawal@amla.io";
                AuthTokenTextBox.Password = "ATATT3xFfGF0mJ9KGAm_XMoI33reNrnaDema2uoHFNdLHNJLeyeha5zm7HZa5KGvdO4w25ezRtVyE-8ovlWBl90wUXfeevfKpuMu0Fkp7KsaK0qyRfFF7jXo2toZlA7IrVPyFdXrCME3CCrMDEmT0n3MzqCj0uJfoDh1XlEY_hX8TuvFkmr2cqI=63E2E4C3";
                OllamaUrlTextBox.Text = "http://localhost:11434";
            }
        }
    }
}
