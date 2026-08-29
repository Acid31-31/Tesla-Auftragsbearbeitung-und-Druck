using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Knowledge
{
    internal sealed class PersistentKnowledgeStore : IKnowledgeStore
    {
        private readonly ConcurrentDictionary<string, KnowledgeEntry> _entries =
            new ConcurrentDictionary<string, KnowledgeEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly string _storePath;
        private readonly object _saveLock = new object();

        public PersistentKnowledgeStore()
        {
            _storePath = Path.Combine(AppInfo.UserDataDirectory, "Data", "knowledge.xml");
            Load();
        }

        public void Upsert(KnowledgeEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Category) || string.IsNullOrWhiteSpace(entry.Key))
                return;

            entry.UpdatedUtc = DateTime.UtcNow;
            _entries[BuildKey(entry.Category, entry.Key)] = entry;
            Save();
        }

        public KnowledgeEntry Find(string category, string key)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(key))
                return null;

            _entries.TryGetValue(BuildKey(category, key), out var entry);
            return entry;
        }

        public IList<KnowledgeEntry> Search(string categoryPrefix, string contains)
        {
            return _entries.Values
                .Where(e => (string.IsNullOrWhiteSpace(categoryPrefix)
                             || e.Category.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase))
                            && (string.IsNullOrWhiteSpace(contains)
                                || (e.Value ?? string.Empty).IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0
                                || (e.Key ?? string.Empty).IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(e => e.UpdatedUtc)
                .ToList();
        }

        private static string BuildKey(string category, string key)
            => category.Trim().ToLowerInvariant() + "::" + key.Trim().ToLowerInvariant();

        private void Load()
        {
            try
            {
                if (!File.Exists(_storePath))
                    return;

                var doc = XDocument.Load(_storePath);
                foreach (var el in doc.Root?.Elements("entry") ?? Enumerable.Empty<XElement>())
                {
                    var entry = new KnowledgeEntry
                    {
                        Category = el.Element("category")?.Value,
                        Key = el.Element("key")?.Value,
                        Value = el.Element("value")?.Value,
                        UpdatedUtc = DateTime.TryParse(el.Element("updatedUtc")?.Value, out var dt) ? dt : DateTime.UtcNow
                    };

                    if (!string.IsNullOrWhiteSpace(entry.Category) && !string.IsNullOrWhiteSpace(entry.Key))
                        _entries[BuildKey(entry.Category, entry.Key)] = entry;
                }
            }
            catch
            {
            }
        }

        private void Save()
        {
            lock (_saveLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_storePath));
                    var root = new XElement("knowledge",
                        _entries.Values.Select(e => new XElement("entry",
                            new XElement("category", e.Category ?? string.Empty),
                            new XElement("key", e.Key ?? string.Empty),
                            new XElement("value", e.Value ?? string.Empty),
                            new XElement("updatedUtc", e.UpdatedUtc.ToString("o")))));

                    new XDocument(root).Save(_storePath);
                }
                catch
                {
                }
            }
        }
    }
}
