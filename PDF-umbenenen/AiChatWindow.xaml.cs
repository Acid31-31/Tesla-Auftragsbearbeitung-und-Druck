using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PDF_umbenenen.Models;

namespace PDF_umbenenen
{
    public partial class AiChatWindow : Window
    {
        private readonly Func<string, CancellationToken, Task<string>> _sendMessageAsync;
        private readonly Func<IReadOnlyList<AiChatMessage>> _getHistory;
        private readonly Func<string> _getStatusText;
        private bool _isSending;

        public AiChatWindow(
            Func<string, CancellationToken, Task<string>> sendMessageAsync,
            Func<IReadOnlyList<AiChatMessage>> getHistory,
            Func<string> getStatusText)
        {
            InitializeComponent();
            _sendMessageAsync = sendMessageAsync ?? throw new ArgumentNullException(nameof(sendMessageAsync));
            _getHistory = getHistory ?? (() => new List<AiChatMessage>());
            _getStatusText = getStatusText ?? (() => string.Empty);

            AiChatStatusTextBlock.Text = _getStatusText();
            RefreshHistoryDisplay();
        }

        public async void AskQuestion(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return;

            AiChatInputTextBox.Text = question.Trim();
            await SendMessageAsync();
        }

        private void RefreshHistoryDisplay()
        {
            var sb = new StringBuilder();
            foreach (var msg in _getHistory())
            {
                var prefix = msg.IsUser ? "Sie" : "KI";
                sb.Append(prefix).Append(": ").AppendLine(msg.Content);
                sb.AppendLine();
            }

            if (sb.Length == 0)
            {
                sb.Append("Stellen Sie eine Frage zur Bedienung, z. B.:");
                sb.AppendLine();
                sb.Append("„Kann ich Wasserzeichen manuell setzen?“");
            }

            AiChatHistoryTextBlock.Text = sb.ToString().TrimEnd();
            ChatScrollViewer.ScrollToEnd();
        }

        private async void AiSendChat_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void AiChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            var message = AiChatInputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message) || _isSending)
                return;

            _isSending = true;
            AiSendChatButton.IsEnabled = false;
            AiChatInputTextBox.Text = string.Empty;

            try
            {
                AiChatStatusTextBlock.Text = _getStatusText();
                await _sendMessageAsync(message, CancellationToken.None);
                RefreshHistoryDisplay();
            }
            catch (Exception ex)
            {
                AiChatHistoryTextBlock.Text += Environment.NewLine + Environment.NewLine + "Fehler: " + ex.Message;
            }
            finally
            {
                _isSending = false;
                AiSendChatButton.IsEnabled = true;
                AiChatInputTextBox.Focus();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
