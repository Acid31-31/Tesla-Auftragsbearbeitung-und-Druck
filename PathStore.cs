using System;
using System.IO;
using System.Xml.Linq;

namespace PDF_umbenenen
{
    internal static class PathStore
    {
        private static readonly string _storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DOK-V01",
            "paths.xml");

        public static string GetOrCreateDefaultWorkspaceFolder()
        {
            var portableFolder = Path.Combine(AppInfo.UserDataDirectory, "Ablage");
            Directory.CreateDirectory(portableFolder);
            return portableFolder;
        }

        public static (string ExcelPath, string PdfFolder, string OrderVerificationPdfPath) Load()
        {
            try
            {
                if (!File.Exists(_storePath))
                    return (string.Empty, GetOrCreateDefaultWorkspaceFolder(), string.Empty);

                var doc = XDocument.Load(_storePath);
                var excel = (string)doc.Root?.Element("ExcelPath") ?? string.Empty;
                var folder = (string)doc.Root?.Element("PdfFolder") ?? string.Empty;
                var orderPdf = (string)doc.Root?.Element("OrderVerificationPdfPath") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(folder))
                {
                    folder = GetOrCreateDefaultWorkspaceFolder();
                }
                else
                {
                    Directory.CreateDirectory(folder);
                }

                return (excel, folder, orderPdf);
            }
            catch
            {
                return (string.Empty, GetOrCreateDefaultWorkspaceFolder(), string.Empty);
            }
        }

        public static void SaveExcel(string excelPath)
        {
            var (_, folder, orderPdf) = Load();
            Save(excelPath, folder, orderPdf);
        }

        public static void SaveFolder(string pdfFolder)
        {
            var (excel, _, orderPdf) = Load();
            Save(excel, pdfFolder, orderPdf);
        }

        public static void SaveOrderVerificationPdf(string orderPdfPath)
        {
            var (excel, folder, _) = Load();
            Save(excel, folder, orderPdfPath);
        }

        private static void Save(string excelPath, string pdfFolder, string orderVerificationPdfPath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_storePath));
                new XDocument(
                    new XElement("Paths",
                        new XElement("ExcelPath", excelPath ?? string.Empty),
                        new XElement("PdfFolder", pdfFolder ?? string.Empty),
                        new XElement("OrderVerificationPdfPath", orderVerificationPdfPath ?? string.Empty)
                    )
                ).Save(_storePath);
            }
            catch { }
        }
    }
}
