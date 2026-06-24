using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal sealed class DataBackupRequest
    {
        public string DestinationRoot { get; set; } = string.Empty;

        public string ExcelPath { get; set; } = string.Empty;

        public string PdfFolder { get; set; } = string.Empty;

        public string OrderVerificationPdfPath { get; set; } = string.Empty;

        public bool CreateZip { get; set; } = true;
    }

    internal static class DataBackupService
    {
        private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static DataBackupResult CreateBackup(DataBackupRequest request)
        {
            var result = new DataBackupResult();

            if (string.IsNullOrWhiteSpace(request.DestinationRoot))
            {
                result.ErrorMessage = "Kein Zielordner angegeben.";
                return result;
            }

            try
            {
                Directory.CreateDirectory(request.DestinationRoot);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var backupName = AppInfo.ProductName + "_Datensicherung_" + timestamp;
                var backupRoot = Path.Combine(request.DestinationRoot, backupName);
                Directory.CreateDirectory(backupRoot);
                result.BackupFolder = backupRoot;

                CopyDirectoryIfExists(
                    Path.Combine(LocalAppData, AppInfo.ProductName),
                    Path.Combine(backupRoot, "AppData", AppInfo.ProductName),
                    "App-Einstellungen (" + AppInfo.ProductName + ")",
                    result);

                CopyDirectoryIfExists(
                    Path.Combine(LocalAppData, "PDF-umbenenen"),
                    Path.Combine(backupRoot, "AppData", "PDF-umbenenen"),
                    "App-Einstellungen (Wasserzeichen/KI)",
                    result);

                var pdfFolder = ResolveExistingDirectory(request.PdfFolder);
                if (pdfFolder != null)
                {
                    CopyDirectory(
                        pdfFolder,
                        Path.Combine(backupRoot, "Arbeitsdaten", "Ablage"),
                        "Ablage-Ordner",
                        result);
                }
                else
                {
                    result.Warnings.Add("Ablage-Ordner nicht gefunden oder leer.");
                }

                CopyFileIfExists(
                    request.ExcelPath,
                    Path.Combine(backupRoot, "Arbeitsdaten", "Excel"),
                    "Excel-Datei",
                    result);

                CopyFileIfExists(
                    request.OrderVerificationPdfPath,
                    Path.Combine(backupRoot, "Arbeitsdaten", "Bestell-PDF"),
                    "Bestell-PDF",
                    result);

                WriteManifest(backupRoot, request, result);

                if (request.CreateZip)
                {
                    var zipPath = backupRoot + ".zip";
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);

                    ZipFile.CreateFromDirectory(backupRoot, zipPath, CompressionLevel.Optimal, false);
                    result.ZipPath = zipPath;
                    result.IncludedSections.Add("ZIP-Archiv");
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static string ResolveExistingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return null;

            return Path.GetFullPath(path);
        }

        private static void CopyDirectoryIfExists(string source, string destination, string label, DataBackupResult result)
        {
            if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
            {
                result.Warnings.Add(label + ": nicht vorhanden.");
                return;
            }

            CopyDirectory(source, destination, label, result);
        }

        private static void CopyDirectory(string source, string destination, string label, DataBackupResult result)
        {
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                result.Warnings.Add(label + ": Ordner ist leer.");
                return;
            }

            foreach (var file in files)
            {
                var relative = GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.Copy(file, target, true);

                var info = new FileInfo(file);
                result.FilesCopied++;
                result.TotalBytes += info.Length;
            }

            result.IncludedSections.Add(label + " (" + files.Length + " Datei(en))");
        }

        private static void CopyFileIfExists(string sourcePath, string destinationFolder, string label, DataBackupResult result)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                result.Warnings.Add(label + ": nicht vorhanden.");
                return;
            }

            Directory.CreateDirectory(destinationFolder);
            var targetPath = Path.Combine(destinationFolder, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, targetPath, true);

            var info = new FileInfo(sourcePath);
            result.FilesCopied++;
            result.TotalBytes += info.Length;
            result.IncludedSections.Add(label + ": " + Path.GetFileName(sourcePath));
        }

        private static void WriteManifest(string backupRoot, DataBackupRequest request, DataBackupResult result)
        {
            var manifestPath = Path.Combine(backupRoot, "BACKUP_INFO.txt");
            var builder = new StringBuilder();
            builder.AppendLine(AppInfo.ProductName + " DATENSICHERUNG");
            builder.AppendLine(new string('=', 32));
            builder.AppendLine("Version:   " + AppInfo.DisplayVersion);
            builder.AppendLine("Edition:   " + AppInfo.EditionLabel);
            builder.AppendLine("Datum:     " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));
            builder.AppendLine("Computer:  " + Environment.MachineName);
            builder.AppendLine("Benutzer:  " + Environment.UserName);
            builder.AppendLine();
            builder.AppendLine("Enthalten:");
            builder.AppendLine("- App-Einstellungen (%LocalAppData%\\" + AppInfo.ProductName + ")");
            builder.AppendLine("- Wasserzeichen/KI (%LocalAppData%\\PDF-umbenenen)");
            builder.AppendLine("- Ablage-Ordner mit PDFs und Excel");
            builder.AppendLine("- Bestell-PDF für Auftragsprüfung (falls vorhanden)");
            builder.AppendLine();
            builder.AppendLine("Quellpfade:");
            builder.AppendLine("- Excel:       " + NullIfEmpty(request.ExcelPath));
            builder.AppendLine("- Ablage:      " + NullIfEmpty(request.PdfFolder));
            builder.AppendLine("- Bestell-PDF: " + NullIfEmpty(request.OrderVerificationPdfPath));
            builder.AppendLine();
            builder.AppendLine("Gesicherte Bereiche:");
            foreach (var section in result.IncludedSections)
                builder.AppendLine("- " + section);

            if (result.Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Hinweise:");
                foreach (var warning in result.Warnings.Distinct())
                    builder.AppendLine("- " + warning);
            }

            builder.AppendLine();
            builder.AppendLine("Wiederherstellung:");
            builder.AppendLine("1. ZIP entpacken oder Ordner kopieren.");
            builder.AppendLine("2. AppData-Inhalt nach %LocalAppData% zurueckkopieren.");
            builder.AppendLine("3. Arbeitsdaten in den gewuenschten Ablage-Ordner legen.");
            builder.AppendLine("4. DOK-V01 starten und Pfade pruefen.");

            File.WriteAllText(manifestPath, builder.ToString(), Encoding.UTF8);
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(nicht gesetzt)" : value;
        }

        private static string GetRelativePath(string root, string fullPath)
        {
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;

            var uriRoot = new Uri(root);
            var uriFile = new Uri(fullPath);
            return Uri.UnescapeDataString(uriRoot.MakeRelativeUri(uriFile).ToString()
                .Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
