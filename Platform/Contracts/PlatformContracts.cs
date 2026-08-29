using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Contracts
{
    /// <summary>Basis für alle austauschbaren Plattform-Module.</summary>
    public interface IPlatformModule
    {
        string ModuleId { get; }

        string DisplayName { get; }

        Version Version { get; }

        void Initialize(IPlatformServices services);

        void Shutdown();
    }

    public interface IPlatformServices
    {
        IConfigurationManager Configuration { get; }

        IContextManager Context { get; }

        IMemoryManager Memory { get; }

        IPromptManager Prompts { get; }

        IPlatformLogger Logger { get; }

        IPluginManager Plugins { get; }
    }

    public interface IAiOrchestrator
    {
        Task<DocumentAnalysisResult> AnalyzeDocumentAsync(string filePath, CancellationToken cancellationToken = default);

        Task<RuleEvaluationResult> EvaluateRulesAsync(DocumentAnalysisResult analysis, CancellationToken cancellationToken = default);

        Task<AiDecisionRecord> DecideAsync(DocumentAnalysisResult analysis, RuleEvaluationResult rules, CancellationToken cancellationToken = default);
    }

    public interface ITaskManager
    {
        string Enqueue(PlatformJob job);

        bool TryGetProgress(string jobId, out PlatformJob job);

        void Cancel(string jobId);
    }

    public interface IContextManager
    {
        string CorrelationId { get; set; }

        IDictionary<string, object> Items { get; }

        void Clear();
    }

    public interface IPromptManager
    {
        string GetPrompt(string promptId);

        void RegisterPrompt(string promptId, string template);
    }

    public interface IMemoryManager
    {
        T Get<T>(string key);

        void Set<T>(string key, T value, TimeSpan? ttl = null);

        void Remove(string key);

        void Clear();
    }

    public interface IConfigurationManager
    {
        T GetSetting<T>(string key, T defaultValue = default);

        void SetSetting<T>(string key, T value);

        void Save();
    }

    public interface IPluginManager
    {
        void Register<TContract>(TContract implementation) where TContract : class;

        TContract Resolve<TContract>() where TContract : class;

        IReadOnlyList<TContract> ResolveAll<TContract>() where TContract : class;
    }

    public interface IDocumentFormatDetector
    {
        DocumentFormat Detect(string filePath);
    }

    public interface IDocumentReader
    {
        DocumentFormat SupportedFormat { get; }

        DocumentReference Open(string filePath);
    }

    public interface IPdfTextExtractor
    {
        string ExtractPageText(string pdfPath, int pageNumber = 1);

        string ExtractAllText(string pdfPath, int maxPages = 10);
    }

    public interface IPdfMetadataReader
    {
        IDictionary<string, string> ReadMetadata(string pdfPath);
    }

    public interface IOcrEngine
    {
        string ProviderId { get; }

        string RecognizeTitleBlock(string filePath, int pageNumber = 1);
    }

    public interface IBarcodeReader
    {
        IList<string> ReadBarcodes(string filePath);
    }

    public interface IQrCodeReader
    {
        IList<string> ReadQrCodes(string filePath);
    }

    public interface IImagePreprocessor
    {
        byte[] PrepareRegion(byte[] imageBytes, string profileId);
    }

    public interface IMultiPageProcessor
    {
        IList<DocumentAnalysisResult> ProcessAllPages(string filePath, CancellationToken cancellationToken);
    }

    public interface IZipProcessor
    {
        IList<string> ExtractToWorkspace(string zipPath, string targetFolder);
    }

    public interface IOfficeDocumentReader
    {
        string ExtractText(string filePath);
    }

    public interface IVisionAnalyzer
    {
        string ProviderId { get; }

        IDictionary<string, string> AnalyzeLayout(string filePath, int pageNumber = 1);
    }

    public interface IDocumentClassifier
    {
        ClassificationResult Classify(DocumentAnalysisResult partialResult);
    }

    public interface IInformationExtractor
    {
        ExtractedDocumentFields Extract(DocumentAnalysisResult partialResult);
    }

    public interface IRuleEngine
    {
        RuleEvaluationResult Evaluate(DocumentAnalysisResult analysis);
    }

    public interface IDecisionEngine
    {
        AiDecisionRecord Decide(DocumentAnalysisResult analysis, RuleEvaluationResult rules);

        IList<SimilarityMatch> FindSimilar(DocumentAnalysisResult analysis, int maxResults = 5);
    }

    public interface IKnowledgeStore
    {
        void Upsert(KnowledgeEntry entry);

        KnowledgeEntry Find(string category, string key);

        IList<KnowledgeEntry> Search(string categoryPrefix, string contains);
    }

    public interface ILearningEngine
    {
        void RecordCorrection(UserCorrection correction);

        void ApplyLearnedHints(ExtractedDocumentFields fields, string documentKey);
    }

    public interface IVectorStore
    {
        void UpsertEmbedding(string key, float[] vector, string summary);

        IList<SimilarityMatch> Search(float[] vector, int maxResults);
    }

    public interface IEmbeddingModelProvider
    {
        string ProviderId { get; }

        float[] Embed(string text);
    }

    public interface IAiModelProvider
    {
        string ProviderId { get; }

        IReadOnlyList<ModelCapability> Capabilities { get; }

        Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
    }

    public interface IVisionModelProvider
    {
        string ProviderId { get; }

        Task<ModelResponse> AnalyzeImagesAsync(ModelRequest request, CancellationToken cancellationToken);
    }

    public interface IModelManager
    {
        void Register(IAiModelProvider provider);

        void Register(IVisionModelProvider provider);

        void Register(IEmbeddingModelProvider provider);

        IAiModelProvider GetTextModel(string hint = null);

        IVisionModelProvider GetVisionModel(string hint = null);

        IEmbeddingModelProvider GetEmbeddingModel(string hint = null);
    }

    public interface IJobQueue
    {
        void Enqueue(PlatformJob job);

        bool TryDequeue(out PlatformJob job);
    }

    public interface IBackgroundProcessor
    {
        void Start(int workerCount);

        void Stop();
    }

    public interface IDatabaseProvider
    {
        string ProviderName { get; }

        void Execute(string sql, IDictionary<string, object> parameters = null);

        IList<IDictionary<string, object>> Query(string sql, IDictionary<string, object> parameters = null);
    }

    public interface IPlatformLogger
    {
        void Info(string category, string message, IDictionary<string, string> data = null);

        void Warning(string category, string message, IDictionary<string, string> data = null);

        void Error(string category, string message, Exception ex = null, IDictionary<string, string> data = null);

        void Audit(AiDecisionRecord record);

        void Performance(string operation, TimeSpan duration, IDictionary<string, string> data = null);

        void UserActivity(string action, string subject = null, IDictionary<string, string> data = null);
    }

    public interface IDocumentAnalysisPipeline
    {
        Task<DocumentAnalysisResult> RunAsync(string filePath, CancellationToken cancellationToken = default);
    }

    public interface IFileOperationService
    {
        string CreateFolder(string path);

        void MoveFile(string source, string target);

        void RenameFile(string source, string newName);

        bool IsDuplicate(string filePath, IEnumerable<string> candidates);
    }
}
