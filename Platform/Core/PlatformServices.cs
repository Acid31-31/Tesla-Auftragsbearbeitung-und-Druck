using System;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class PlatformServices : IPlatformServices
    {
        public PlatformServices(
            IConfigurationManager configuration,
            IContextManager context,
            IMemoryManager memory,
            IPromptManager prompts,
            IPlatformLogger logger,
            IPluginManager plugins)
        {
            Configuration = configuration;
            Context = context;
            Memory = memory;
            Prompts = prompts;
            Logger = logger;
            Plugins = plugins;
        }

        public IConfigurationManager Configuration { get; }

        public IContextManager Context { get; }

        public IMemoryManager Memory { get; }

        public IPromptManager Prompts { get; }

        public IPlatformLogger Logger { get; }

        public IPluginManager Plugins { get; }
    }
}
