using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class IndexManager : IIndexManager
    {
        private readonly ConcurrentDictionary<string, string> _index =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void IndexDocument(string key, DocumentAnalysisResult result)
        {
            if (string.IsNullOrWhiteSpace(key) || result == null)
                return;

            var text = (result.PlainText ?? string.Empty)
                + " " + (result.Fields?.DrawingNumber ?? string.Empty)
                + " " + (result.Fields?.Material ?? string.Empty)
                + " " + (result.Fields?.Customer ?? string.Empty)
                + " " + (result.Classification?.Kind.ToString() ?? string.Empty);

            _index[NormalizeKey(key)] = text.Trim();
        }

        public IList<string> Search(string query, int maxResults = 20)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            var terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return _index
                .Where(pair => terms.All(t => pair.Value.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(pair => Score(pair.Value, terms))
                .Take(Math.Max(1, maxResults))
                .Select(pair => pair.Key)
                .ToList();
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _index.TryRemove(NormalizeKey(key), out _);
        }

        private static string NormalizeKey(string key)
            => Path.GetFileName(key ?? string.Empty).ToLowerInvariant();

        private static int Score(string text, string[] terms)
        {
            var score = 0;
            foreach (var term in terms)
            {
                var idx = 0;
                while ((idx = text.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    score++;
                    idx += term.Length;
                }
            }

            return score;
        }
    }
}
