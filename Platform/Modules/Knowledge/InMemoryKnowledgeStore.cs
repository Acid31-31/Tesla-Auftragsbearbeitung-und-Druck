using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Knowledge
{
    internal sealed class InMemoryKnowledgeStore : IKnowledgeStore
    {
        private readonly ConcurrentDictionary<string, KnowledgeEntry> _entries =
            new ConcurrentDictionary<string, KnowledgeEntry>(StringComparer.OrdinalIgnoreCase);

        public void Upsert(KnowledgeEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Category) || string.IsNullOrWhiteSpace(entry.Key))
                return;

            entry.UpdatedUtc = DateTime.UtcNow;
            _entries[BuildKey(entry.Category, entry.Key)] = entry;
        }

        public KnowledgeEntry Find(string category, string key)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(key))
                return null;

            return _entries.TryGetValue(BuildKey(category, key), out var entry) ? entry : null;
        }

        public IList<KnowledgeEntry> Search(string categoryPrefix, string contains)
        {
            return _entries.Values
                .Where(e => (string.IsNullOrWhiteSpace(categoryPrefix)
                             || e.Category.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase))
                            && (string.IsNullOrWhiteSpace(contains)
                                || (e.Value?.IndexOf(contains, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0))
                .ToList();
        }

        private static string BuildKey(string category, string key) => category + "::" + key;
    }
}
