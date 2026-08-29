using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class PromptManager : Contracts.IPromptManager
    {
        private readonly ConcurrentDictionary<string, string> _prompts =
            new ConcurrentDictionary<string, string>();

        public PromptManager()
        {
            RegisterPrompt(
                "drawing.vision",
                "Analysiere technische Blechzeichnung. Liefere JSON mit isAssembly, partName, drawingNumber, material, thicknessMm, confidence.");
            RegisterPrompt(
                "document.classify",
                "Klassifiziere Geschäftsdokument. Liefere JSON mit kind und confidence.");
        }

        public string GetPrompt(string promptId)
        {
            if (string.IsNullOrWhiteSpace(promptId))
                return string.Empty;

            return _prompts.TryGetValue(promptId, out var value) ? value : string.Empty;
        }

        public void RegisterPrompt(string promptId, string template)
        {
            if (string.IsNullOrWhiteSpace(promptId))
                return;

            _prompts[promptId] = template ?? string.Empty;
        }
    }
}
