using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Sammelt Beispieldateien für die Kundenanlage (PDF/Excel/ZIP/TXT) in einem Temp-Ordner.
    /// </summary>
    internal sealed class CustomerOnboardingStagingService
    {
        private readonly string _rootFolder;
        private readonly List<string> _files = new List<string>();

        public CustomerOnboardingStagingService()
        {
            _rootFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DOK-V01",
                "CustomerLearn",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootFolder);
        }

        public string StagingFolder => _rootFolder;

        public IReadOnlyList<string> Files => _files;

        public string ExcelPath =>
            _files.FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f)?.ToLowerInvariant();
                return ext == ".xlsx" || ext == ".xls";
            });

        public string OrderPdfPath =>
            _files.FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f)?.ToLowerInvariant();
                return ext == ".pdf"
                       && OrderVerificationFileHelper.IsOrderVerificationFileName(Path.GetFileName(f));
            });

        public string OrderTextPath =>
            _files.FirstOrDefault(f => OrderVerificationFileHelper.IsOrderTextExtension(f));

        public int AddPaths(IEnumerable<string> paths)
        {
            var added = 0;
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly))
                        added += AddFile(file) ? 1 : 0;
                    continue;
                }

                if (AddFile(path))
                    added++;
            }

            return added;
        }

        public void DisposeFolder()
        {
            try
            {
                if (Directory.Exists(_rootFolder))
                    Directory.Delete(_rootFolder, true);
            }
            catch
            {
            }
        }

        private bool AddFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
            if (ext == ".zip")
                return ExtractZip(path) > 0;

            if (ext != ".pdf" && ext != ".xlsx" && ext != ".xls" && ext != ".txt" && ext != ".csv")
                return false;

            var target = GetUniqueTargetPath(Path.GetFileName(path));
            File.Copy(path, target, overwrite: false);
            if (!_files.Any(f => string.Equals(f, target, StringComparison.OrdinalIgnoreCase)))
                _files.Add(target);
            return true;
        }

        private int ExtractZip(string zipPath)
        {
            var extractDir = Path.Combine(_rootFolder, "zip_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(extractDir);
            try
            {
                AppSecurityService.ExtractZipSafely(zipPath, extractDir);
            }
            catch
            {
                return 0;
            }

            var count = 0;
            foreach (var file in Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file)?.ToLowerInvariant() ?? string.Empty;
                if (ext == ".zip")
                {
                    count += ExtractZip(file);
                    continue;
                }

                if (ext != ".pdf" && ext != ".xlsx" && ext != ".xls" && ext != ".txt" && ext != ".csv")
                    continue;

                var target = GetUniqueTargetPath(Path.GetFileName(file));
                try
                {
                    File.Copy(file, target, overwrite: false);
                    if (!_files.Any(f => string.Equals(f, target, StringComparison.OrdinalIgnoreCase)))
                    {
                        _files.Add(target);
                        count++;
                    }
                }
                catch
                {
                }
            }

            return count;
        }

        private string GetUniqueTargetPath(string fileName)
        {
            var safe = AppSecurityService.SanitizeFileName(fileName);
            if (string.IsNullOrWhiteSpace(safe))
                safe = "datei";

            var target = Path.Combine(_rootFolder, safe);
            if (!File.Exists(target))
                return target;

            var name = Path.GetFileNameWithoutExtension(safe);
            var ext = Path.GetExtension(safe);
            var i = 2;
            while (true)
            {
                target = Path.Combine(_rootFolder, name + "_" + i + ext);
                if (!File.Exists(target))
                    return target;
                i++;
            }
        }
    }
}
