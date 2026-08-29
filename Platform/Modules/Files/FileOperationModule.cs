using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Files
{
    internal sealed class FileOperationModule : IFileOperationService
    {
        public string CreateFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            Directory.CreateDirectory(path);
            return path;
        }

        public void MoveFile(string source, string target)
        {
            if (!File.Exists(source))
                return;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(target))
                File.Delete(target);

            File.Move(source, target);
        }

        public void RenameFile(string source, string newName)
        {
            if (!File.Exists(source) || string.IsNullOrWhiteSpace(newName))
                return;

            var target = Path.Combine(Path.GetDirectoryName(source) ?? string.Empty, newName);
            MoveFile(source, target);
        }

        public bool IsDuplicate(string filePath, IEnumerable<string> candidates)
        {
            if (!File.Exists(filePath) || candidates == null)
                return false;

            var hash = ComputeHash(filePath);
            return candidates
                .Where(File.Exists)
                .Any(c => !string.Equals(c, filePath, StringComparison.OrdinalIgnoreCase)
                          && ComputeHash(c) == hash);
        }

        private static string ComputeHash(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var bytes = sha.ComputeHash(stream);
                return BitConverter.ToString(bytes).Replace("-", string.Empty);
            }
        }
    }

    internal sealed class DuplicateDetectorModule : IDuplicateDetector
    {
        private readonly IFileOperationService _files;

        public DuplicateDetectorModule(IFileOperationService files)
        {
            _files = files;
        }

        public bool IsDuplicate(string filePath, IEnumerable<string> candidates)
            => _files?.IsDuplicate(filePath, candidates) ?? false;

        public IList<string> FindDuplicates(IEnumerable<string> filePaths)
        {
            var list = filePaths?.Where(File.Exists).ToList() ?? new List<string>();
            var duplicates = new List<string>();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    if (_files.IsDuplicate(list[i], new[] { list[j] }))
                    {
                        if (!duplicates.Contains(list[j], StringComparer.OrdinalIgnoreCase))
                            duplicates.Add(list[j]);
                    }
                }
            }

            return duplicates;
        }
    }
}
