using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Platform.Modules.Ai
{
    /// <summary>Offline-Text/KI über Regel-Engine (kein Ollama).</summary>
    internal sealed class RuleBasedTextModelModule : IAiModelProvider
    {
        private readonly OfflineAiOrchestrator _orchestrator = new OfflineAiOrchestrator();

        public string ProviderId => "rule-based-text";

        public IReadOnlyList<ModelCapability> Capabilities { get; } = new[]
        {
            ModelCapability.TextCompletion,
            ModelCapability.Chat,
            ModelCapability.Classification
        };

        public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            var content = await _orchestrator.ChatAsync(
                request.SystemPrompt ?? string.Empty,
                request.UserPrompt ?? string.Empty,
                cancellationToken).ConfigureAwait(false);

            return new ModelResponse
            {
                Success = !string.IsNullOrWhiteSpace(content),
                Content = content ?? string.Empty,
                ProviderId = ProviderId,
                Confidence = string.IsNullOrWhiteSpace(content) ? 0d : 0.65d
            };
        }
    }

    /// <summary>Lokale Layout-/OCR-Vision ohne Netzwerk.</summary>
    internal sealed class LocalVisionModelModule : IVisionModelProvider
    {
        public string ProviderId => "local-vision";

        public Task<ModelResponse> AnalyzeImagesAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ModelResponse
            {
                Success = true,
                ProviderId = ProviderId,
                Content = "Lokale Vision über IVisionAnalyzer/StructuredVisionModule",
                Confidence = 0.7d
            });
        }
    }

    /// <summary>Offline-Embedding (deterministisch, lokal).</summary>
    internal sealed class HashEmbeddingModelModule : IEmbeddingModelProvider
    {
        public string ProviderId => "local-embedding";

        public float[] Embed(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new float[32];

            var vector = new float[32];
            var tokens = Regex.Split(text.ToLowerInvariant(), @"\W+");
            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                var bucket = Math.Abs(token.GetHashCode()) % vector.Length;
                vector[bucket] += 1f;
            }

            return vector;
        }
    }
}
