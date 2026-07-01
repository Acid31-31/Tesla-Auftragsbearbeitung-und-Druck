using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;

namespace PDF_umbenenen.Services
{
    internal static class LicenseContentService
    {
        public static string LoadLicenseText()
        {
            var fromFile = TryLoadFromFile();
            if (!string.IsNullOrWhiteSpace(fromFile))
                return fromFile;

            var fromResource = TryLoadFromEmbeddedResource();
            if (!string.IsNullOrWhiteSpace(fromResource))
                return fromResource;

            return "Copyright © " + DateTime.Now.Year + " Alexander Hölzer\nAlle Rechte vorbehalten.";
        }

        public static string GetCopyrightSummary()
        {
            return "Copyright © 2026 Alexander Hölzer · Alle Rechte vorbehalten · Proprietäre Software";
        }

        private static string TryLoadFromFile()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir;
            var candidates = new[]
            {
                Path.Combine(baseDir, "LICENSE_DE.txt"),
                Path.Combine(baseDir, "LICENSE.txt"),
                Path.Combine(assemblyDir, "LICENSE_DE.txt"),
                Path.Combine(assemblyDir, "LICENSE.txt"),
                Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "LICENSE_DE.txt"))
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
                catch
                {
                }
            }

            return null;
        }

        private static string TryLoadFromEmbeddedResource()
        {
            try
            {
                if (Application.Current != null)
                {
                    var packUri = new Uri("pack://application:,,,/LICENSE_DE.txt", UriKind.Absolute);
                    var resource = Application.GetResourceStream(packUri);
                    if (resource?.Stream != null)
                    {
                        using (var reader = new StreamReader(resource.Stream, Encoding.UTF8))
                            return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
            }

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith("LICENSE_DE.txt", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using (var stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream == null)
                            continue;

                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
