using System.Collections.Generic;
using PDF_umbenenen.Platform.Contracts;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class ModelLoader : IModelLoader
    {
        private readonly IModelManager _models;
        private readonly HashSet<string> _loaded = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public ModelLoader(IModelManager models)
        {
            _models = models;
        }

        public bool TryLoad(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                return false;

            var text = _models.GetTextModel(providerId);
            if (text != null && text.ProviderId.Equals(providerId, System.StringComparison.OrdinalIgnoreCase))
            {
                _loaded.Add(providerId);
                return true;
            }

            var vision = _models.GetVisionModel(providerId);
            if (vision != null && vision.ProviderId.Equals(providerId, System.StringComparison.OrdinalIgnoreCase))
            {
                _loaded.Add(providerId);
                return true;
            }

            var embed = _models.GetEmbeddingModel(providerId);
            if (embed != null && embed.ProviderId.Equals(providerId, System.StringComparison.OrdinalIgnoreCase))
            {
                _loaded.Add(providerId);
                return true;
            }

            return false;
        }

        public IReadOnlyList<string> GetLoadedProviders()
        {
            return new List<string>(_loaded);
        }
    }

    internal sealed class ModelSwitcher : IModelSwitcher
    {
        private readonly IModelManager _models;

        public ModelSwitcher(IModelManager models)
        {
            _models = models;
        }

        public string ActiveTextProviderId { get; private set; } = "rule-based-text";

        public string ActiveVisionProviderId { get; private set; } = "local-vision";

        public string ActiveEmbeddingProviderId { get; private set; } = "local-embedding";

        public void SwitchTextModel(string providerId)
        {
            if (_models.GetTextModel(providerId) != null)
                ActiveTextProviderId = providerId;
        }

        public void SwitchVisionModel(string providerId)
        {
            if (_models.GetVisionModel(providerId) != null)
                ActiveVisionProviderId = providerId;
        }

        public void SwitchEmbeddingModel(string providerId)
        {
            if (_models.GetEmbeddingModel(providerId) != null)
                ActiveEmbeddingProviderId = providerId;
        }
    }
}
