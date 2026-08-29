using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class ContextManager : Contracts.IContextManager
    {
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

        public IDictionary<string, object> Items { get; } = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public void Clear()
        {
            Items.Clear();
            CorrelationId = Guid.NewGuid().ToString("N");
        }
    }
}
