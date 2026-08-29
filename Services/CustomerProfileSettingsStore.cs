using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal static class CustomerProfileSettingsStore
    {
        private const string WatermarkFileName = "watermark-settings.xml";
        private const string AiFileName = "ai-settings.xml";

        private static string ProfileDirectory(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return string.Empty;

            return Path.Combine(AppInfo.UserDataDirectory, "Customers", profileId.Trim());
        }

        public static bool HasSnapshot(string profileId)
        {
            var dir = ProfileDirectory(profileId);
            if (string.IsNullOrWhiteSpace(dir))
                return false;

            return File.Exists(Path.Combine(dir, WatermarkFileName))
                   || File.Exists(Path.Combine(dir, AiFileName));
        }

        public static void SaveSnapshotFromGlobal(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return;

            var dir = ProfileDirectory(profileId);
            Directory.CreateDirectory(dir);

            CopyIfExists(UserSettingsPath.GetFilePath(WatermarkFileName), Path.Combine(dir, WatermarkFileName));
            CopyIfExists(UserSettingsPath.GetFilePath(AiFileName), Path.Combine(dir, AiFileName));
        }

        public static bool TryApplyToGlobal(string profileId)
        {
            if (!HasSnapshot(profileId))
                return false;

            var dir = ProfileDirectory(profileId);
            var applied = false;

            var watermarkPath = Path.Combine(dir, WatermarkFileName);
            if (File.Exists(watermarkPath))
            {
                File.Copy(watermarkPath, UserSettingsPath.GetFilePath(WatermarkFileName), true);
                applied = true;
            }

            var aiPath = Path.Combine(dir, AiFileName);
            if (File.Exists(aiPath))
            {
                File.Copy(aiPath, UserSettingsPath.GetFilePath(AiFileName), true);
                applied = true;
            }

            return applied;
        }

        public static string GetSummary(string profileId)
        {
            if (!HasSnapshot(profileId))
                return "Keine gespeicherten Einstellungen – beim Anlegen oder über „Einstellungen übernehmen“ hinterlegen.";

            var dir = ProfileDirectory(profileId);
            var lines = new StringBuilder();
            lines.AppendLine("Gespeicherte Einstellungen für dieses Profil:");

            var watermarkPath = Path.Combine(dir, WatermarkFileName);
            if (File.Exists(watermarkPath))
            {
                var wm = ReadWatermarkSnapshot(watermarkPath);
                lines.AppendLine("• Wasserzeichen: "
                                 + (wm.WatermarksEnabled ? "an" : "aus")
                                 + ", intelligent: "
                                 + (wm.UseSmartPlacement ? "an" : "aus")
                                 + ", Material: "
                                 + (wm.ExtractMaterialType ? "an" : "aus")
                                 + "/" + (wm.ExtractMaterialThickness ? "Stärke an" : "Stärke aus")
                                 + ", Position: "
                                 + wm.DataPositionDisplayName);
            }

            var aiPath = Path.Combine(dir, AiFileName);
            if (File.Exists(aiPath))
            {
                var ai = ReadAiSnapshot(aiPath);
                lines.AppendLine("• KI/OCR: KI "
                                 + (ai.AiEnabled ? "an" : "aus")
                                 + ", Vision "
                                 + (ai.LocalVisionEnabled ? "an" : "aus")
                                 + ", OCR "
                                 + (ai.LocalOcrEnabled ? "an" : "aus")
                                 + ", Sprachen: "
                                 + (string.IsNullOrWhiteSpace(ai.OcrLanguages) ? "deu+eng" : ai.OcrLanguages));
            }

            return lines.ToString().TrimEnd();
        }

        public static void DeleteForProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return;

            try
            {
                var dir = ProfileDirectory(profileId);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
            }
        }

        private static void CopyIfExists(string sourcePath, string targetPath)
        {
            if (!File.Exists(sourcePath))
                return;

            File.Copy(sourcePath, targetPath, true);
        }

        private static WatermarkSettings ReadWatermarkSnapshot(string path)
        {
            try
            {
                var root = XDocument.Load(path).Root;
                if (root == null)
                    return new WatermarkSettings();

                WatermarkDataPosition dataPosition;
                OrderNumberPosition orderPosition;
                Enum.TryParse((string)root.Element("DataPosition"), true, out dataPosition);
                Enum.TryParse((string)root.Element("OrderPosition"), true, out orderPosition);

                return new WatermarkSettings
                {
                    WatermarksEnabled = !string.Equals((string)root.Element("WatermarksEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    UseSmartPlacement = !string.Equals((string)root.Element("UseSmartPlacement"), "false", StringComparison.OrdinalIgnoreCase),
                    ExtractMaterialType = !string.Equals((string)root.Element("ExtractMaterialType"), "false", StringComparison.OrdinalIgnoreCase),
                    ExtractMaterialThickness = !string.Equals((string)root.Element("ExtractMaterialThickness"), "false", StringComparison.OrdinalIgnoreCase),
                    ExtractPowderCoating = string.Equals((string)root.Element("ExtractPowderCoating"), "true", StringComparison.OrdinalIgnoreCase),
                    PowderCoatingShowRalNumber = !string.Equals((string)root.Element("PowderCoatingShowRalNumber"), "false", StringComparison.OrdinalIgnoreCase),
                    ExtractAnodizing = string.Equals((string)root.Element("ExtractAnodizing"), "true", StringComparison.OrdinalIgnoreCase),
                    DataPosition = Enum.IsDefined(typeof(WatermarkDataPosition), dataPosition)
                        ? dataPosition
                        : WatermarkDataPosition.LeftMiddleVertical,
                    OrderPosition = Enum.IsDefined(typeof(OrderNumberPosition), orderPosition)
                        ? orderPosition
                        : OrderNumberPosition.TopRight
                };
            }
            catch
            {
                return new WatermarkSettings();
            }
        }

        private static AiSettings ReadAiSnapshot(string path)
        {
            try
            {
                var root = XDocument.Load(path).Root;
                if (root == null)
                    return new AiSettings();

                return new AiSettings
                {
                    AiEnabled = !string.Equals((string)root.Element("AiEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    LocalOcrEnabled = !string.Equals((string)root.Element("LocalOcrEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    TesseractPath = (string)root.Element("TesseractPath") ?? string.Empty,
                    OcrLanguages = (string)root.Element("OcrLanguages") ?? "deu+eng",
                    LocalVisionEnabled = !string.Equals((string)root.Element("LocalVisionEnabled"), "false", StringComparison.OrdinalIgnoreCase)
                };
            }
            catch
            {
                return new AiSettings();
            }
        }
    }
}
