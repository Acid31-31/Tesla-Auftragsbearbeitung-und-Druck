using System;
using System.IO;
using System.Xml.Linq;

namespace PDF_umbenenen
{
    internal static class PathStore
    {
        private static readonly string _storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDF-umbenenen",
            "paths.xml");

        public static string GetOrCreateDefaultWorkspaceFolder()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var portableFolder = Path.Combine(baseDir, "Ablage");
            Directory.CreateDirectory(portableFolder);
            return portableFolder;
        }

        public static (string ExcelPath, string PdfFolder) Load()
        {
            try
            {
                if (!File.Exists(_storePath))
                    return (string.Empty, GetOrCreateDefaultWorkspaceFolder());

                var doc = XDocument.Load(_storePath);
                var excel = (string)doc.Root?.Element("ExcelPath") ?? string.Empty;
                var folder = (string)doc.Root?.Element("PdfFolder") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(folder))
                {
                    folder = GetOrCreateDefaultWorkspaceFolder();
                }
                else
                {
                    Directory.CreateDirectory(folder);
                }

                return (excel, folder);
            }
            catch
            {
                return (string.Empty, GetOrCreateDefaultWorkspaceFolder());
            }
        }

        public static void SaveExcel(string excelPath)
        {
            var (_, folder) = Load();
            Save(excelPath, folder);
        }

        public static void SaveFolder(string pdfFolder)
        {
            var (excel, _) = Load();
            Save(excel, pdfFolder);
        }

        private static void Save(string excelPath, string pdfFolder)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_storePath));
                new XDocument(
                    new XElement("Paths",
                        new XElement("ExcelPath", excelPath ?? string.Empty),
                        new XElement("PdfFolder", pdfFolder ?? string.Empty)
                    )
                ).Save(_storePath);
            }
            catch { }
        }
    }
}
