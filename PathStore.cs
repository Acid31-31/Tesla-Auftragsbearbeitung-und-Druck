using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace PDF_umbenenen
{
    internal static class PathStore
    {
        private static string StorePath => Path.Combine(AppInfo.UserDataDirectory, "paths.xml");

        public static string GetOrCreateDefaultWorkspaceFolder()
        {
            var workspaceFolder = AppInfo.WorkspaceDirectory;
            Directory.CreateDirectory(workspaceFolder);
            return workspaceFolder;
        }

        /// <summary>
        /// Alle bekannten Ablage-Ordner (aktuell, gespeichert, Legacy neben EXE / alter App-Name).
        /// </summary>
        public static IEnumerable<string> GetAllKnownWorkspaceFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddIfExists(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (Directory.Exists(fullPath))
                        folders.Add(fullPath);
                }
                catch
                {
                }
            }

            AddIfExists(GetOrCreateDefaultWorkspaceFolder());

            try
            {
                if (File.Exists(StorePath))
                {
                    var doc = XDocument.Load(StorePath);
                    var savedFolder = (string)doc.Root?.Element("PdfFolder");
                    AddIfExists(savedFolder);
                }
            }
            catch
            {
            }

            AddIfExists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ablage"));
            AddIfExists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PDF-umbenenen",
                "Ablage"));

            return folders;
        }

        public static void ClearProcessingPaths()
        {
            // Zielordner (manuelle Auswahl) bewusst behalten
            var (_, _, _, returnFolder, returnName) = Load();
            var folder = GetOrCreateDefaultWorkspaceFolder();
            Save(string.Empty, folder, string.Empty, returnFolder, returnName);
        }

        public static (
            string ExcelPath,
            string PdfFolder,
            string OrderVerificationPdfPath,
            string OrderReturnFolder,
            string OrderReturnFileName) Load()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return (string.Empty, GetOrCreateDefaultWorkspaceFolder(), string.Empty, string.Empty, string.Empty);

                var doc = XDocument.Load(StorePath);
                var excel = (string)doc.Root?.Element("ExcelPath") ?? string.Empty;
                var folder = (string)doc.Root?.Element("PdfFolder") ?? string.Empty;
                var orderPdf = (string)doc.Root?.Element("OrderVerificationPdfPath") ?? string.Empty;
                var orderReturnFolder = (string)doc.Root?.Element("OrderReturnFolder") ?? string.Empty;
                var orderReturnFileName = (string)doc.Root?.Element("OrderReturnFileName") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(folder))
                {
                    folder = GetOrCreateDefaultWorkspaceFolder();
                }
                else
                {
                    Directory.CreateDirectory(folder);
                }

                return (excel, folder, orderPdf, orderReturnFolder, orderReturnFileName);
            }
            catch
            {
                return (string.Empty, GetOrCreateDefaultWorkspaceFolder(), string.Empty, string.Empty, string.Empty);
            }
        }

        public static void SaveExcel(string excelPath)
        {
            var (_, folder, orderPdf, returnFolder, returnName) = Load();
            Save(excelPath, folder, orderPdf, returnFolder, returnName);
        }

        public static void SaveFolder(string pdfFolder)
        {
            var (excel, _, orderPdf, returnFolder, returnName) = Load();
            Save(excel, pdfFolder, orderPdf, returnFolder, returnName);
        }

        public static void SaveOrderVerificationPdf(string orderPdfPath)
        {
            var (excel, folder, _, returnFolder, returnName) = Load();
            Save(excel, folder, orderPdfPath, returnFolder, returnName);
        }

        public static void SaveOrderReturnLocation(string folder, string fileName)
        {
            var (excel, pdfFolder, orderPdf, _, _) = Load();
            Save(excel, pdfFolder, orderPdf, folder ?? string.Empty, fileName ?? string.Empty);
        }

        public static void ClearOrderReturnLocation()
        {
            SaveOrderReturnLocation(string.Empty, string.Empty);
        }

        private static void Save(
            string excelPath,
            string pdfFolder,
            string orderVerificationPdfPath,
            string orderReturnFolder,
            string orderReturnFileName)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                new XDocument(
                    new XElement("Paths",
                        new XElement("ExcelPath", excelPath ?? string.Empty),
                        new XElement("PdfFolder", pdfFolder ?? string.Empty),
                        new XElement("OrderVerificationPdfPath", orderVerificationPdfPath ?? string.Empty),
                        new XElement("OrderReturnFolder", orderReturnFolder ?? string.Empty),
                        new XElement("OrderReturnFileName", orderReturnFileName ?? string.Empty)
                    )
                ).Save(StorePath);
            }
            catch { }
        }
    }
}
