using System;

namespace PDF_umbenenen.Services.Ai
{
    internal static class OllamaUrlHelper
    {
        public const string DefaultBaseUrl = "http://127.0.0.1:11434";

        public static string NormalizeBaseUrl(string raw)
        {
            var value = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                return DefaultBaseUrl;

            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "http://" + value;
            }

            return value.TrimEnd('/');
        }

        public static bool IsTrustedLocalUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return false;

            if (!Uri.TryCreate(NormalizeBaseUrl(baseUrl), UriKind.Absolute, out var uri))
                return false;

            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                return false;

            var host = uri.Host ?? string.Empty;
            return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                   || host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)
                   || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
