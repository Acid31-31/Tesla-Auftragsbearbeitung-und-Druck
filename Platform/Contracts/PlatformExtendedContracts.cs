using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Contracts
{
    public interface ICacheManager
    {
        T GetOrAdd<T>(string key, Func<T> factory, TimeSpan? ttl = null);

        void Invalidate(string keyPrefix);

        void Clear();
    }

    public interface IIndexManager
    {
        void IndexDocument(string key, DocumentAnalysisResult result);

        IList<string> Search(string query, int maxResults = 20);

        void Remove(string key);
    }

    public interface IResourceMonitor
    {
        ResourceSnapshot GetSnapshot();

        bool PreferGpu { get; }

        int RecommendedWorkerCount { get; }
    }

    public interface IJobScheduler
    {
        string Schedule(PlatformJob job, int priority = 0);

        void Cancel(string jobId);

        IList<PlatformJob> GetPending();
    }

    public interface ISemanticClassifier
    {
        ClassificationResult ClassifySemantic(string text, DocumentKind hint = DocumentKind.Unknown);
    }

    public interface IDuplicateDetector
    {
        bool IsDuplicate(string filePath, IEnumerable<string> candidates);

        IList<string> FindDuplicates(IEnumerable<string> filePaths);
    }

    public interface IConflictResolver
    {
        ExtractedDocumentFields Resolve(ExtractedDocumentFields primary, ExtractedDocumentFields secondary);
    }

    public interface IRecommendationEngine
    {
        IList<string> RecommendActions(DocumentAnalysisResult analysis, RuleEvaluationResult rules);
    }

    public interface IConfidenceScorer
    {
        double ScoreFields(ExtractedDocumentFields fields);

        double ScoreDecision(AiDecisionRecord record);
    }

    public interface IModelLoader
    {
        bool TryLoad(string providerId);

        IReadOnlyList<string> GetLoadedProviders();
    }

    public interface IModelSwitcher
    {
        void SwitchTextModel(string providerId);

        void SwitchVisionModel(string providerId);

        void SwitchEmbeddingModel(string providerId);

        string ActiveTextProviderId { get; }

        string ActiveVisionProviderId { get; }

        string ActiveEmbeddingProviderId { get; }
    }

    public interface IStructuredVisionAnalyzer
    {
        VisionLayoutResult AnalyzeStructured(string filePath, int pageNumber = 1);
    }

    public interface IDatabaseProviderFactory
    {
        IDatabaseProvider Create(string providerName, string connectionString = null);
    }

    public interface IUserManager
    {
        bool Authenticate(string userName, string password);

        PlatformUser GetCurrentUser();

        void SetCurrentUser(PlatformUser user);
    }

    public interface IRoleManager
    {
        bool HasPermission(string roleId, string permission);

        IReadOnlyList<string> GetPermissions(string roleId);
    }

    public interface IDataEncryption
    {
        string Encrypt(string plainText);

        string Decrypt(string cipherText);
    }

    public interface IPlatformBackupService
    {
        string CreateBackup(string targetFolder);

        void Restore(string backupFolder);
    }

    public interface IPlatformApi
    {
        void Start(int port = 8765);

        void Stop();

        bool IsRunning { get; }
    }

    public interface ILocalApiHost
    {
        string Invoke(string method, IDictionary<string, string> parameters);
    }

    public interface IPluginApiHost
    {
        void RegisterPlugin(string pluginId, Func<IDictionary<string, string>, string> handler);

        string InvokePlugin(string pluginId, IDictionary<string, string> parameters);
    }

    public interface IImportApi
    {
        int ImportKnowledge(string filePath);

        int ImportRules(string filePath);
    }

    public interface IExportApi
    {
        string ExportKnowledge(string targetFolder);

        string ExportAuditLog(string targetFolder);
    }

    public interface IBatchJobProcessor
    {
        void Start(int workerCount);

        void Stop();

        string EnqueueBatch(IList<string> filePaths, string jobType = "analyze");

        bool TryGetJob(string jobId, out PlatformJob job);
    }

    internal interface IPlatformFacade
    {
        IAiOrchestrator Orchestrator { get; }

        Services.Ai.OfflineAiOrchestrator LegacyAi { get; }

        void SyncSettings();
    }
}
