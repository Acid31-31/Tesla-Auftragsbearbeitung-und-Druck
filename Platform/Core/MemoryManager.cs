using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class MemoryManager : Contracts.IMemoryManager
    {
        private sealed class Entry
        {
            public object Value;
            public DateTime? ExpiresUtc;
        }

        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public T Get<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !_entries.TryGetValue(key, out var entry))
                return default;

            if (entry.ExpiresUtc.HasValue && entry.ExpiresUtc.Value < DateTime.UtcNow)
            {
                _entries.TryRemove(key, out _);
                return default;
            }

            return entry.Value is T typed ? typed : default;
        }

        public void Set<T>(string key, T value, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _entries[key] = new Entry
            {
                Value = value,
                ExpiresUtc = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : (DateTime?)null
            };
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _entries.TryRemove(key, out _);
        }

        public void Clear() => _entries.Clear();
    }
}
