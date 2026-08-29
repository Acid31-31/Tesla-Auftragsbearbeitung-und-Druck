using System;
using System.Collections.Concurrent;
using PDF_umbenenen.Platform.Contracts;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class CacheManager : ICacheManager
    {
        private readonly IMemoryManager _memory;

        public CacheManager(IMemoryManager memory)
        {
            _memory = memory;
        }

        public T GetOrAdd<T>(string key, Func<T> factory, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                return factory != null ? factory() : default;

            var cached = _memory.Get<T>(key);
            if (cached != null && !cached.Equals(default(T)))
                return cached;

            var value = factory != null ? factory() : default;
            if (value != null)
                _memory.Set(key, value, ttl ?? TimeSpan.FromMinutes(30));

            return value;
        }

        public void Invalidate(string keyPrefix)
        {
            if (string.IsNullOrWhiteSpace(keyPrefix))
                return;

            _memory.Remove(keyPrefix);
        }

        public void Clear() => _memory.Clear();
    }
}
