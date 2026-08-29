using System;
using System.Collections.Generic;
using PDF_umbenenen.Platform.Bridge;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Logging;
using PDF_umbenenen.Platform.Modules.Ai;
using PDF_umbenenen.Platform.Modules.Api;
using PDF_umbenenen.Platform.Modules.Decisions;
using PDF_umbenenen.Platform.Modules.Documents;
using PDF_umbenenen.Platform.Modules.Extraction;
using PDF_umbenenen.Platform.Modules.Files;
using PDF_umbenenen.Platform.Modules.Knowledge;
using PDF_umbenenen.Platform.Modules.Learning;
using PDF_umbenenen.Platform.Modules.Rules;
using PDF_umbenenen.Platform.Modules.Security;
using PDF_umbenenen.Platform.Modules.Storage;
using PDF_umbenenen.Platform.Modules.Cli;
using PDF_umbenenen.Platform.Modules.Vision;

namespace PDF_umbenenen.Platform.Core
{
    public static class PlatformHost
    {
        private static readonly object InitLock = new object();
        private static bool _initialized;

        public static IAiOrchestrator Orchestrator { get; private set; }

        internal static IPlatformFacade Facade { get; private set; }

        public static IPluginManager Plugins { get; private set; }

        public static IModelManager Models { get; private set; }

        public static IKnowledgeStore Knowledge { get; private set; }

        public static ILearningEngine Learning { get; private set; }

        public static IVectorStore Vectors { get; private set; }

        public static ITaskManager Tasks { get; private set; }

        public static IPlatformLogger Logger { get; private set; }

        public static IDocumentAnalysisPipeline DocumentPipeline { get; private set; }

        public static IConfigurationManager Configuration { get; private set; }

        public static IBatchJobProcessor BatchProcessor { get; private set; }

        public static IResourceMonitor Resources { get; private set; }

        public static IPlatformApi Api { get; private set; }

        public static IFileOperationService Files { get; private set; }

        public static void Initialize()
        {
            lock (InitLock)
            {
                if (_initialized)
                    return;

                var plugins = new PluginManager();
                var logger = new PlatformLogger();
                var configuration = new ConfigurationManager();
                var memory = new MemoryManager();
                var services = new PlatformServices(
                    configuration,
                    new ContextManager(),
                    memory,
                    new PromptManager(),
                    logger,
                    plugins);

                Configuration = configuration;
                Models = new ModelManager();
                Knowledge = new PersistentKnowledgeStore();
                Learning = new FeedbackLearningEngine(Knowledge);
                Vectors = new InMemoryVectorStore();
                Tasks = new TaskManager();
                Resources = new ResourceMonitor();
                Files = new FileOperationModule();

                var cache = new CacheManager(memory);
                var index = new IndexManager();
                var scheduler = new JobScheduler();
                var confidence = new ConfidenceScorerModule();
                var localEmbedding = new HashEmbeddingModelModule();

                RegisterModules(plugins, Models, services, index, localEmbedding, scheduler);

                plugins.Register<IKnowledgeStore>(Knowledge);
                plugins.Register<ILearningEngine>(Learning);
                plugins.Register<IVectorStore>(Vectors);
                plugins.Register<ICacheManager>(cache);
                plugins.Register<IIndexManager>(index);
                plugins.Register<IJobScheduler>(scheduler);
                plugins.Register<IConfidenceScorer>(confidence);
                plugins.Register<IResourceMonitor>(Resources);
                plugins.Register<IFileOperationService>(Files);

                DocumentPipeline = new DocumentAnalysisPipeline(
                    plugins, logger, index, Vectors, localEmbedding, confidence);

                var decisions = plugins.Resolve<IDecisionEngine>();
                Orchestrator = new PlatformOrchestrator(
                    DocumentPipeline,
                    plugins.Resolve<IRuleEngine>(),
                    decisions,
                    logger,
                    plugins.Resolve<IRecommendationEngine>(),
                    decisions);

                Facade = new PlatformAiFacade(Orchestrator, configuration);
                BatchProcessor = new BatchJobProcessor(Orchestrator, scheduler, Tasks, logger, Resources);

                Api = new PlatformApiHost(Orchestrator, Knowledge, logger);
                plugins.Register<IPlatformApi>(Api);
                plugins.Register<ILocalApiHost>((ILocalApiHost)Api);
                plugins.Register<IPluginApiHost>((IPluginApiHost)Api);
                plugins.Register<IImportApi>((IImportApi)Api);
                plugins.Register<IExportApi>((IExportApi)Api);

                Plugins = plugins;
                Logger = logger;

                var modelLoader = new ModelLoader(Models);
                var modelSwitcher = new ModelSwitcher(Models);
                plugins.Register<IModelLoader>(modelLoader);
                plugins.Register<IModelSwitcher>(modelSwitcher);

                modelLoader.TryLoad("rule-based-text");
                modelLoader.TryLoad("local-vision");
                modelLoader.TryLoad("local-embedding");
                modelLoader.TryLoad("tesseract-cli");

                if (configuration.GetSetting("api.enabled", false))
                    Api.Start(configuration.GetSetting("api.port", 8765));

                BatchProcessor.Start(Resources.RecommendedWorkerCount);
                _initialized = true;

                logger.Info("platform", "DOK-V01 KI-Plattform initialisiert (offline, CLI-Module, ohne Ollama)");
            }
        }

        public static void Shutdown()
        {
            BatchProcessor?.Stop();
            Api?.Stop();
            Configuration?.Save();
        }

        private static void RegisterModules(
            IPluginManager plugins,
            IModelManager models,
            IPlatformServices services,
            IIndexManager index,
            HashEmbeddingModelModule localEmbedding,
            IJobScheduler scheduler)
        {
            plugins.Register<IDocumentFormatDetector>(new DocumentFormatDetectorModule());
            plugins.Register<IDocumentReader>(new PdfDocumentReaderModule());
            plugins.Register<IPdfTextExtractor>(new PdfTextExtractorModule());
            plugins.Register<IPdfMetadataReader>(new PdfMetadataReaderModule());
            plugins.Register<IOcrEngine>(new TesseractTitleBlockOcrModule());
            plugins.Register<IOcrEngine>(new TesseractOcrCliModule());
            plugins.Register<IOfficeDocumentReader>(new OfficeDocumentReaderModule());
            plugins.Register<IZipProcessor>(new ZipProcessorModule());
            plugins.Register<IBarcodeReader>(new BarcodeReaderModule());
            plugins.Register<IQrCodeReader>(new QrCodeReaderModule());
            plugins.Register<IImagePreprocessor>(new ImagePreprocessorModule());
            plugins.Register<IMultiPageProcessor>(new MultiPageProcessorModule(plugins));
            plugins.Register<IVisionAnalyzer>(new LocalDrawingVisionModule());
            plugins.Register<IStructuredVisionAnalyzer>(new StructuredVisionModule());
            plugins.Register<IDocumentClassifier>(new HeuristicDocumentClassifierModule());
            plugins.Register<IInformationExtractor>(new DrawingInformationExtractorModule());
            plugins.Register<IRuleEngine>(new BusinessRuleEngineModule());
            plugins.Register<ISemanticClassifier>(new SemanticClassifierModule());
            plugins.Register<IConflictResolver>(new ConflictResolverModule());
            plugins.Register<IRecommendationEngine>(new RecommendationEngineModule());
            plugins.Register<IDuplicateDetector>(new DuplicateDetectorModule(new FileOperationModule()));
            plugins.Register<IDecisionEngine>(new PlatformDecisionEngineModule(Vectors, Knowledge, localEmbedding));
            plugins.Register<IDatabaseProvider>(new DatabaseProviderFactory().Create("sqlite"));
            plugins.Register<IDatabaseProviderFactory>(new DatabaseProviderFactory());
            plugins.Register<IUserManager>(new UserManagerModule());
            plugins.Register<IRoleManager>(new RoleManagerModule());
            plugins.Register<IDataEncryption>(new DataEncryptionModule());
            plugins.Register<IPlatformBackupService>(new PlatformBackupServiceModule());

            var ruleText = new RuleBasedTextModelModule();
            var localVision = new LocalVisionModelModule();

            models.Register(ruleText);
            models.Register(localVision);
            models.Register(localEmbedding);

            plugins.Register<IAiModelProvider>(ruleText);
            plugins.Register<IVisionModelProvider>(localVision);
            plugins.Register<IEmbeddingModelProvider>(localEmbedding);
        }
    }
}
