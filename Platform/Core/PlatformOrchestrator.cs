using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class PlatformOrchestrator : IAiOrchestrator
    {
        private readonly IDocumentAnalysisPipeline _pipeline;
        private readonly IRuleEngine _rules;
        private readonly IDecisionEngine _decisions;
        private readonly IPlatformLogger _logger;
        private readonly IRecommendationEngine _recommendations;
        private readonly IDecisionEngine _similarityEngine;

        public PlatformOrchestrator(
            IDocumentAnalysisPipeline pipeline,
            IRuleEngine rules,
            IDecisionEngine decisions,
            IPlatformLogger logger,
            IRecommendationEngine recommendations,
            IDecisionEngine similarityEngine)
        {
            _pipeline = pipeline;
            _rules = rules;
            _decisions = decisions;
            _logger = logger;
            _recommendations = recommendations;
            _similarityEngine = similarityEngine;
        }

        public Task<DocumentAnalysisResult> AnalyzeDocumentAsync(string filePath, CancellationToken cancellationToken = default)
            => _pipeline.RunAsync(filePath, cancellationToken);

        public Task<RuleEvaluationResult> EvaluateRulesAsync(DocumentAnalysisResult analysis, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _rules?.Evaluate(analysis) ?? new RuleEvaluationResult();
            }, cancellationToken);
        }

        public Task<AiDecisionRecord> DecideAsync(
            DocumentAnalysisResult analysis,
            RuleEvaluationResult rules,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = _decisions?.Decide(analysis, rules) ?? new AiDecisionRecord();

                var recommendations = _recommendations?.RecommendActions(analysis, rules);
                if (recommendations != null && recommendations.Count > 0)
                    record.Evidence["recommendations"] = string.Join(",", recommendations);

                var similar = _similarityEngine?.FindSimilar(analysis, 3);
                if (similar != null && similar.Count > 0)
                    record.Evidence["similarCount"] = similar.Count.ToString();

                _logger.Audit(record);
                return record;
            }, cancellationToken);
        }
    }
}
