using System;

namespace PDF_umbenenen.Models
{
    public sealed class AiChatMessage
    {
        public AiChatMessage(string role, string content)
        {
            Role = role ?? "user";
            Content = content ?? string.Empty;
            Timestamp = DateTime.Now;
        }

        public string Role { get; }

        public string Content { get; set; }

        public DateTime Timestamp { get; }

        public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

        public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase);
    }
}
