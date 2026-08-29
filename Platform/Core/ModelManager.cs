using System;
using System.Collections.Generic;
using System.Linq;
using PDF_umbenenen.Platform.Contracts;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class ModelManager : IModelManager
    {
        private readonly List<IAiModelProvider> _textModels = new List<IAiModelProvider>();
        private readonly List<IVisionModelProvider> _visionModels = new List<IVisionModelProvider>();
        private readonly List<IEmbeddingModelProvider> _embeddingModels = new List<IEmbeddingModelProvider>();

        public void Register(IAiModelProvider provider)
        {
            if (provider != null)
                _textModels.Add(provider);
        }

        public void Register(IVisionModelProvider provider)
        {
            if (provider != null)
                _visionModels.Add(provider);
        }

        public void Register(IEmbeddingModelProvider provider)
        {
            if (provider != null)
                _embeddingModels.Add(provider);
        }

        public IAiModelProvider GetTextModel(string hint = null)
            => Pick(_textModels, hint);

        public IVisionModelProvider GetVisionModel(string hint = null)
            => Pick(_visionModels, hint);

        public IEmbeddingModelProvider GetEmbeddingModel(string hint = null)
            => Pick(_embeddingModels, hint);

        private static T Pick<T>(IList<T> items, string hint) where T : class
        {
            if (items == null || items.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(hint))
            {
                var match = items.FirstOrDefault(x =>
                    string.Equals(GetProviderId(x), hint, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return items.Last();
        }

        private static string GetProviderId(object provider)
        {
            if (provider is IAiModelProvider text)
                return text.ProviderId;
            if (provider is IVisionModelProvider vision)
                return vision.ProviderId;
            if (provider is IEmbeddingModelProvider embedding)
                return embedding.ProviderId;
            return string.Empty;
        }
    }
}
