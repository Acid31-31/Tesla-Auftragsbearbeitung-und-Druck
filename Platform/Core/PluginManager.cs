using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class PluginManager : Contracts.IPluginManager
    {
        private readonly ConcurrentDictionary<Type, List<object>> _registry =
            new ConcurrentDictionary<Type, List<object>>();

        public void Register<TContract>(TContract implementation) where TContract : class
        {
            if (implementation == null)
                throw new ArgumentNullException(nameof(implementation));

            var list = _registry.GetOrAdd(typeof(TContract), _ => new List<object>());
            lock (list)
            {
                list.RemoveAll(x => ReferenceEquals(x, implementation));
                list.Add(implementation);
            }
        }

        public TContract Resolve<TContract>() where TContract : class
        {
            if (!_registry.TryGetValue(typeof(TContract), out var list))
                return null;

            lock (list)
            {
                return list.OfType<TContract>().LastOrDefault();
            }
        }

        public IReadOnlyList<TContract> ResolveAll<TContract>() where TContract : class
        {
            if (!_registry.TryGetValue(typeof(TContract), out var list))
                return Array.Empty<TContract>();

            lock (list)
            {
                return list.OfType<TContract>().ToList();
            }
        }
    }
}
