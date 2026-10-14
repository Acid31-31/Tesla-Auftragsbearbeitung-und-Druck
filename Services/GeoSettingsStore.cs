using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PDF_umbenenen.Services
{
    internal sealed class GeoSettings
    {
        public List<string> SearchFolders { get; set; } = new List<string>();

        /// <summary>Leer = Standard unter Ablage\GEO\&lt;Auftrag&gt;.</summary>
        public string TargetBaseFolder { get; set; } = string.Empty;
    }

    internal static class GeoSettingsStore
    {
        private static string StorePath => UserSettingsPath.GetFilePath("geo-settings.xml");

        public static GeoSettings Load()
        {
            var settings = new GeoSettings();
            try
            {
                // Fallback: ältere Ablage direkt unter UserDataDirectory
                var path = StorePath;
                if (!File.Exists(path))
                {
                    var legacy = Path.Combine(AppInfo.UserDataDirectory, "geo-settings.xml");
                    if (File.Exists(legacy))
                        path = legacy;
                    else
                        return settings;
                }

                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root == null)
                    return settings;

                settings.TargetBaseFolder = (string)root.Element("TargetBaseFolder") ?? string.Empty;
                var folders = root.Element("SearchFolders");
                if (folders != null)
                {
                    foreach (var el in folders.Elements("Folder"))
                    {
                        var folderPath = (el.Value ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(folderPath))
                            continue;
                        if (settings.SearchFolders.Any(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        settings.SearchFolders.Add(folderPath);
                    }
                }
            }
            catch
            {
            }

            return settings;
        }

        public static void Save(GeoSettings settings)
        {
            if (settings == null)
                settings = new GeoSettings();

            var folders = (settings.SearchFolders ?? new List<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var path = StorePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var root = new XElement("GeoSettings",
                new XElement("TargetBaseFolder", settings.TargetBaseFolder ?? string.Empty),
                new XElement("SearchFolders",
                    folders.Select(f => new XElement("Folder", f))));

            new XDocument(root).Save(path);
        }

        public static string ResolveTargetFolder(string orderNumber)
        {
            var settings = Load();
            var baseFolder = (settings.TargetBaseFolder ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseFolder))
                baseFolder = Path.Combine(PathStore.GetOrCreateDefaultWorkspaceFolder(), "GEO");

            Directory.CreateDirectory(baseFolder);

            var safeOrder = AppSecurityService.SanitizeFileName(
                string.IsNullOrWhiteSpace(orderNumber) ? "Auftrag" : orderNumber.Trim());
            var target = Path.Combine(baseFolder, safeOrder);
            Directory.CreateDirectory(target);
            return target;
        }
    }
}
