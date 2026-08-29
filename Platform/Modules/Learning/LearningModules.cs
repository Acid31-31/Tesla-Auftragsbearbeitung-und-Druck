using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Learning
{
    internal sealed class FeedbackLearningEngine : ILearningEngine
    {
        private readonly IKnowledgeStore _knowledge;

        public FeedbackLearningEngine(IKnowledgeStore knowledge)
        {
            _knowledge = knowledge;
        }

        public void RecordCorrection(UserCorrection correction)
        {
            if (correction == null || string.IsNullOrWhiteSpace(correction.DocumentKey))
                return;

            _knowledge.Upsert(new KnowledgeEntry
            {
                Category = "correction." + (correction.FieldName ?? "field"),
                Key = correction.DocumentKey,
                Value = correction.NewValue ?? string.Empty
            });
        }

        public void ApplyLearnedHints(ExtractedDocumentFields fields, string documentKey)
        {
            if (fields == null || string.IsNullOrWhiteSpace(documentKey))
                return;

            ApplyFieldHint(fields, documentKey, "material", v => fields.Material = v);
            ApplyFieldHint(fields, documentKey, "materialThickness", v => fields.MaterialThickness = v);
            ApplyFieldHint(fields, documentKey, "drawingNumber", v => fields.DrawingNumber = v);
        }

        private void ApplyFieldHint(ExtractedDocumentFields fields, string documentKey, string fieldName, Action<string> apply)
        {
            var entry = _knowledge.Find("correction." + fieldName, documentKey);
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Value))
                apply(entry.Value);
        }
    }

    internal sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly ConcurrentDictionary<string, float[]> _vectors =
            new ConcurrentDictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, string> _summaries =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void UpsertEmbedding(string key, float[] vector, string summary)
        {
            if (string.IsNullOrWhiteSpace(key) || vector == null)
                return;

            _vectors[key] = vector;
            _summaries[key] = summary ?? string.Empty;
        }

        public IList<SimilarityMatch> Search(float[] vector, int maxResults)
        {
            if (vector == null || vector.Length == 0)
                return new List<SimilarityMatch>();

            return _vectors
                .Select(pair => new SimilarityMatch
                {
                    DocumentKey = pair.Key,
                    Score = CosineSimilarity(vector, pair.Value),
                    Summary = _summaries.TryGetValue(pair.Key, out var summary) ? summary : string.Empty
                })
                .OrderByDescending(m => m.Score)
                .Take(Math.Max(1, maxResults))
                .ToList();
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null)
                return 0d;

            var len = Math.Min(a.Length, b.Length);
            double dot = 0d;
            double magA = 0d;
            double magB = 0d;
            for (var i = 0; i < len; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            if (magA <= 0d || magB <= 0d)
                return 0d;

            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }
    }
}
