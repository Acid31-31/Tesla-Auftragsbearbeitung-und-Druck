using System;
using System.Collections.Generic;
using System.Linq;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Decisions
{
    internal sealed class PlatformDecisionEngineModule : IDecisionEngine
    {
        private readonly IVectorStore _vectors;
        private readonly IKnowledgeStore _knowledge;
        private readonly IEmbeddingModelProvider _embedder;
        private readonly IConfidenceScorer _confidence;

        public PlatformDecisionEngineModule(
            IVectorStore vectors,
            IKnowledgeStore knowledge,
            IEmbeddingModelProvider embedder)
        {
            _vectors = vectors;
            _knowledge = knowledge;
            _embedder = embedder;
            _confidence = new ConfidenceScorerModule();
        }

        public AiDecisionRecord Decide(DocumentAnalysisResult analysis, RuleEvaluationResult rules)
        {
            if (rules != null && rules.Matched && rules.Priority >= 80)
            {
                return BuildRecord(
                    analysis,
                    rules.Action,
                    0.95d,
                    "rule-engine",
                    rules.Message,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ruleId"] = rules.RuleId ?? string.Empty
                    });
            }

            var similar = FindSimilar(analysis, 3);
            var confidence = _confidence.ScoreFields(analysis?.Fields);
            if (analysis?.Fields != null)
                analysis.Fields.Confidence = confidence;

            if (similar.Count > 0 && similar[0].Score >= 0.85d)
            {
                return BuildRecord(
                    analysis,
                    "apply-similar-precedent",
                    Math.Max(confidence, similar[0].Score),
                    "similarity-analysis",
                    "Ähnliches Dokument gefunden",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["similarKey"] = similar[0].DocumentKey,
                        ["similarScore"] = similar[0].Score.ToString("0.###")
                    });
            }

            var decision = confidence >= 0.7d ? "accept-extraction" : "manual-review";
            return BuildRecord(
                analysis,
                decision,
                confidence,
                "decision-engine",
                confidence >= 0.7d ? "Extraktion ausreichend sicher" : "Unsichere Extraktion – manuelle Prüfung");
        }

        public IList<SimilarityMatch> FindSimilar(DocumentAnalysisResult analysis, int maxResults = 5)
        {
            var text = analysis?.PlainText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return new List<SimilarityMatch>();

            var embedder = _embedder ?? new Modules.Ai.HashEmbeddingModelModule();
            var vector = embedder.Embed(text);
            return _vectors?.Search(vector, maxResults) ?? new List<SimilarityMatch>();
        }

        private static AiDecisionRecord BuildRecord(
            DocumentAnalysisResult analysis,
            string decision,
            double confidence,
            string provider,
            string rationale,
            IDictionary<string, string> evidence = null)
        {
            return new AiDecisionRecord
            {
                Subject = analysis?.Document?.FilePath,
                Decision = decision,
                Confidence = confidence,
                ProviderId = provider,
                Rationale = rationale,
                Evidence = evidence ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
