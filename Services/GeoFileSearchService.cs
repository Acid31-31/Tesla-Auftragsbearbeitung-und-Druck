using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal sealed class GeoFileInfo
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DrawingNumber { get; set; } = string.Empty;
        public string DrawingRevision { get; set; } = string.Empty;
        public string BgNumber { get; set; } = string.Empty;
        public string BgRevision { get; set; } = string.Empty;
        public string ThicknessMm { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string MaterialToken { get; set; } = string.Empty;
        /// <summary>Dateierstellung (UTC) – bei mehreren Treffern die neueste GEO wählen.</summary>
        public DateTime CreatedUtc { get; set; }
        public DateTime LastWriteUtc { get; set; }
    }

    internal sealed class GeoQueryItem
    {
        public string DrawingNumber { get; set; } = string.Empty;
        public string DrawingRevision { get; set; } = string.Empty;
        public List<string> BgNumbers { get; set; } = new List<string>();
        public string ThicknessMm { get; set; } = string.Empty;
        public string RequestedAmount { get; set; } = string.Empty;
        public int RequestedQuantity { get; set; }
    }

    internal sealed class GeoCollectResult
    {
        public string DrawingNumber { get; set; } = string.Empty;
        public string DrawingRevision { get; set; } = string.Empty;
        public string BgNumbersText { get; set; } = string.Empty;
        public string ThicknessMm { get; set; } = string.Empty;
        public string RequestedAmount { get; set; } = string.Empty;
        public int RequestedQuantity { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool Found { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string CopyPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ThicknessSortKey { get; set; } = string.Empty;
    }

    internal static class GeoFileSearchService
    {
        // TSL-1227-125_REV00_V2a-1,5mm_80x.GEO  bzw. …_R00_BG-…_R00_…_20x.geo
        // Revision: _R00 oder _REV00 (Groß/Klein egal)
        private static readonly Regex DrawingRevRegex = new Regex(
            @"^(?<draw>(?:[A-Z]{2,8}-)?\d{3,8}-\d{2,8}(?:-[A-Z0-9]+)?|[A-Z]{2,8}\d{0,2}(?:[\s\-_/]+\d{1,6}){2,4})_(?:REV|R)(?<rev>\d{1,3})(?![0-9A-Z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BgRevRegex = new Regex(
            @"_BG-(?<bg>(?:[A-Z]{2,8}-)?\d{3,8}-\d{2,8}(?:-[A-Z0-9]+)?|\d{3,8}-\d{2,8})_(?:REV|R)(?<rev>\d{1,3})(?![0-9A-Z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThicknessRegex = new Regex(
            @"(?<mm>\d+(?:[.,]\d+)?)\s*mm",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Stückzahl am Ende: _80x.geo / _12x.GEO – wird immer auf Bestellmenge gesetzt
        private static readonly Regex QuantityRegex = new Regex(
            @"_(?<qty>\d+)x(?=\.geo$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MaterialTokenRegex = new Regex(
            @"_(?<mat>[A-Z]{1,4}\d?[a-z]?)-\d+(?:[.,]\d+)?mm",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryParseFileName(string fileNameOrPath, out GeoFileInfo info)
        {
            info = null;
            var name = Path.GetFileName(fileNameOrPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (!name.EndsWith(".geo", StringComparison.OrdinalIgnoreCase))
                return false;

            var stem = Path.GetFileNameWithoutExtension(name);
            var drawMatch = DrawingRevRegex.Match(stem);
            if (!drawMatch.Success)
                return false;

            info = new GeoFileInfo
            {
                FileName = name,
                FullPath = fileNameOrPath ?? string.Empty,
                DrawingNumber = drawMatch.Groups["draw"].Value.Trim().ToUpperInvariant(),
                DrawingRevision = "R" + drawMatch.Groups["rev"].Value.PadLeft(2, '0')
            };

            var bgMatch = BgRevRegex.Match(stem);
            if (bgMatch.Success)
            {
                info.BgNumber = bgMatch.Groups["bg"].Value.Trim().ToUpperInvariant();
                info.BgRevision = "R" + bgMatch.Groups["rev"].Value.PadLeft(2, '0');
            }

            var thick = ThicknessRegex.Match(stem);
            if (thick.Success)
            {
                info.ThicknessMm = thick.Groups["mm"].Value.Replace(',', '.') + "mm";
            }

            var mat = MaterialTokenRegex.Match(stem);
            if (mat.Success)
                info.MaterialToken = mat.Groups["mat"].Value;

            var qty = QuantityRegex.Match(name);
            if (qty.Success
                && int.TryParse(qty.Groups["qty"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q))
                info.Quantity = q;

            return true;
        }

        public static string ApplyQuantityToFileName(string fileName, int quantity)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return fileName;
            if (quantity < 0)
                quantity = 0;

            var name = Path.GetFileName(fileName);
            var ext = Path.GetExtension(name);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".geo";

            var qtyToken = "_" + quantity.ToString(CultureInfo.InvariantCulture) + "x";

            // Nur die Zahl vor dem x ersetzen (_80x → _12x), Rest unverändert
            if (QuantityRegex.IsMatch(name))
                return QuantityRegex.Replace(name, qtyToken);

            if (name.EndsWith(".geo", StringComparison.OrdinalIgnoreCase))
            {
                var stem = Path.GetFileNameWithoutExtension(name);
                return stem + qtyToken + ext;
            }

            return name + qtyToken + ext;
        }

        public static int ParseRequestedQuantity(string amountText)
        {
            if (string.IsNullOrWhiteSpace(amountText))
                return 0;

            var text = amountText.Trim();
            var m = Regex.Match(text, @"(\d+(?:[.,]\d+)?)");
            if (!m.Success)
                return 0;

            if (decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return (int)Math.Round(d, MidpointRounding.AwayFromZero);

            return 0;
        }

        private static readonly Regex FolderThicknessNameRegex = new Regex(
            @"^(\d+(?:[.,]\d+)?)\s*mm$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private sealed class GeoScanJob
        {
            public string Path { get; set; } = string.Empty;
            public List<string> Needles { get; set; }
            public HashSet<string> WantedKeys { get; set; }
            public string Label { get; set; } = string.Empty;
        }

        public static List<GeoFileInfo> ScanFolders(IEnumerable<string> folders)
        {
            return ScanFolders(folders, queries: null, progress: null);
        }

        /// <summary>
        /// Rekursiver GEO-Scan. Mit Queries: zuerst gezielt in mm-Unterordnern
        /// (z. B. Stahl\3mm), sonst Fallback auf den Hauptordner – und nur Dateien,
        /// deren Name zu einer gesuchten Zeichnung passen kann.
        /// </summary>
        public static List<GeoFileInfo> ScanFolders(
            IEnumerable<string> folders,
            IList<GeoQueryItem> queries,
            Action<string> progress = null)
        {
            var roots = (folders ?? Enumerable.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f))
                .Select(f => Path.GetFullPath(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (roots.Count == 0)
                return new List<GeoFileInfo>();

            var jobs = BuildThicknessScopedScanJobs(roots, queries, progress);
            if (jobs.Count == 0)
                return new List<GeoFileInfo>();

            var results = new ConcurrentBag<GeoFileInfo>();
            var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            var dirsScanned = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Math.Min(8, Environment.ProcessorCount))
            };

            Parallel.ForEach(jobs, parallelOptions, job =>
            {
                CollectGeoFilesSafe(job.Path, seen, results, job.Needles, job.WantedKeys, () =>
                {
                    var n = System.Threading.Interlocked.Increment(ref dirsScanned);
                    if (n == 1 || n % 250 == 0)
                        progress?.Invoke("Ordner geprüft: " + n.ToString("N0", CultureInfo.InvariantCulture));
                });
            });

            progress?.Invoke("GEO-Kandidaten: " + results.Count.ToString("N0", CultureInfo.InvariantCulture)
                + " · Ordner: " + dirsScanned.ToString("N0", CultureInfo.InvariantCulture));

            return results.ToList();
        }

        /// <summary>
        /// Pro Hauptordner: passende Direkt-Unterordner nach Stärke (1mm, 3,0mm, …);
        /// Zeichnungen ohne Stärke oder ohne Treffer-Ordner → Fallback ganzer Hauptordner.
        /// </summary>
        private static List<GeoScanJob> BuildThicknessScopedScanJobs(
            IList<string> roots,
            IList<GeoQueryItem> queries,
            Action<string> progress)
        {
            if (queries == null || queries.Count == 0)
            {
                return roots.Select(r => new GeoScanJob
                {
                    Path = r,
                    Needles = null,
                    WantedKeys = null,
                    Label = r
                }).ToList();
            }

            var withThickness = new List<Tuple<GeoQueryItem, double>>();
            var withoutThickness = new List<GeoQueryItem>();

            foreach (var q in queries)
            {
                if (q == null || string.IsNullOrWhiteSpace(q.DrawingNumber))
                    continue;

                if (TryParseThicknessMmValue(q.ThicknessMm, out var mm))
                    withThickness.Add(Tuple.Create(q, mm));
                else
                    withoutThickness.Add(q);
            }

            var jobs = new List<GeoScanJob>();
            var targetedLabels = new List<string>();

            foreach (var root in roots)
            {
                var childDirs = ListImmediateSubdirectories(root);
                var thicknessChildren = new List<Tuple<string, double>>();
                foreach (var child in childDirs)
                {
                    var name = Path.GetFileName(child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (TryParseThicknessFromFolderName(name, out var folderMm))
                        thicknessChildren.Add(Tuple.Create(child, folderMm));
                }

                var matchedMm = new HashSet<double>();
                var byMm = withThickness.GroupBy(t => Math.Round(t.Item2, 2));

                foreach (var group in byMm)
                {
                    var mm = group.Key;
                    var matchingDirs = thicknessChildren
                        .Where(t => ThicknessValuesEqual(t.Item2, mm))
                        .Select(t => t.Item1)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (matchingDirs.Count == 0)
                        continue;

                    matchedMm.Add(mm);
                    var subset = group.Select(t => t.Item1).ToList();
                    var needles = BuildFilenameNeedles(subset);
                    var keys = BuildWantedDrawingKeys(subset);

                    foreach (var dir in matchingDirs)
                    {
                        jobs.Add(new GeoScanJob
                        {
                            Path = dir,
                            Needles = needles,
                            WantedKeys = keys,
                            Label = dir
                        });
                        targetedLabels.Add(Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                    }
                }

                var fallback = new List<GeoQueryItem>();
                fallback.AddRange(withoutThickness);
                foreach (var group in byMm)
                {
                    if (!matchedMm.Any(m => ThicknessValuesEqual(m, group.Key)))
                        fallback.AddRange(group.Select(t => t.Item1));
                }

                if (fallback.Count > 0)
                {
                    jobs.Add(new GeoScanJob
                    {
                        Path = root,
                        Needles = BuildFilenameNeedles(fallback),
                        WantedKeys = BuildWantedDrawingKeys(fallback),
                        Label = root + " (Fallback)"
                    });
                }
            }

            if (targetedLabels.Count > 0)
            {
                var unique = targetedLabels.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
                progress?.Invoke("Gezielt nach Stärke: " + string.Join(", ", unique));
            }
            else
            {
                progress?.Invoke("Keine mm-Unterordner gefunden – Suche in Hauptordnern…");
            }

            return MergeScanJobsByPath(jobs);
        }

        private static List<GeoScanJob> MergeScanJobsByPath(List<GeoScanJob> jobs)
        {
            if (jobs == null || jobs.Count <= 1)
                return jobs ?? new List<GeoScanJob>();

            var map = new Dictionary<string, GeoScanJob>(StringComparer.OrdinalIgnoreCase);
            foreach (var job in jobs)
            {
                if (job == null || string.IsNullOrWhiteSpace(job.Path))
                    continue;

                if (!map.TryGetValue(job.Path, out var existing))
                {
                    map[job.Path] = job;
                    continue;
                }

                existing.Needles = UnionNeedles(existing.Needles, job.Needles);
                existing.WantedKeys = UnionKeys(existing.WantedKeys, job.WantedKeys);
            }

            return map.Values.ToList();
        }

        private static List<string> UnionNeedles(List<string> a, List<string> b)
        {
            if (a == null && b == null)
                return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (a != null)
                foreach (var x in a) set.Add(x);
            if (b != null)
                foreach (var x in b) set.Add(x);
            return set.Count == 0 ? null : set.OrderByDescending(n => n.Length).ToList();
        }

        private static HashSet<string> UnionKeys(HashSet<string> a, HashSet<string> b)
        {
            if (a == null && b == null)
                return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (a != null)
                foreach (var x in a) set.Add(x);
            if (b != null)
                foreach (var x in b) set.Add(x);
            return set;
        }

        private static List<string> ListImmediateSubdirectories(string root)
        {
            var list = new List<string>();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(root))
                {
                    if (ShouldSkipDirectory(sub))
                        continue;
                    list.Add(sub);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }

            return list;
        }

        private static bool TryParseThicknessMmValue(string text, out double mm)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var match = ThicknessRegex.Match(text);
            if (match.Success)
            {
                return double.TryParse(
                    match.Groups["mm"].Value.Replace(',', '.'),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out mm);
            }

            var bare = Regex.Match(text.Trim(), @"^(\d+(?:[.,]\d+)?)$", RegexOptions.CultureInvariant);
            if (!bare.Success)
                return false;

            return double.TryParse(
                bare.Groups[1].Value.Replace(',', '.'),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out mm);
        }

        private static bool TryParseThicknessFromFolderName(string folderName, out double mm)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(folderName))
                return false;

            var match = FolderThicknessNameRegex.Match(folderName.Trim());
            if (!match.Success)
                return false;

            return double.TryParse(
                match.Groups[1].Value.Replace(',', '.'),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out mm);
        }

        private static bool ThicknessValuesEqual(double a, double b)
        {
            return Math.Round(a, 2) == Math.Round(b, 2);
        }

        /// <summary>
        /// Rekursiv ohne AllDirectories – überspringt gesperrte Pfade/Junctions.
        /// Optional nur Dateinamen, die zu den Auftrags-Zeichnungen passen können.
        /// </summary>
        private static void CollectGeoFilesSafe(
            string rootFolder,
            ConcurrentDictionary<string, byte> seen,
            ConcurrentBag<GeoFileInfo> results,
            IList<string> filenameNeedles,
            HashSet<string> wantedDrawingKeys,
            Action onDirectoryVisited)
        {
            var stack = new Stack<string>();
            stack.Push(rootFolder);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                onDirectoryVisited?.Invoke();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*.geo"))
                    {
                        if (!seen.TryAdd(file, 0))
                            continue;

                        var name = Path.GetFileName(file);
                        if (!FileNameMightMatchNeedles(name, filenameNeedles))
                            continue;

                        if (!TryParseFileName(file, out var info) || info == null)
                            continue;

                        if (wantedDrawingKeys != null && wantedDrawingKeys.Count > 0)
                        {
                            var key = OrderVerificationDrawingKey.Normalize(info.DrawingNumber);
                            if (string.IsNullOrWhiteSpace(key) || !wantedDrawingKeys.Contains(key))
                                continue;
                        }

                        info.FullPath = file;
                        // Zeitstempel erst hier – nur für echte Kandidaten (Netzwerk-IO sparen)
                        try
                        {
                            info.CreatedUtc = File.GetCreationTimeUtc(file);
                            info.LastWriteUtc = File.GetLastWriteTimeUtc(file);
                        }
                        catch
                        {
                            info.CreatedUtc = DateTime.MinValue;
                            info.LastWriteUtc = DateTime.MinValue;
                        }

                        results.Add(info);
                    }

                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        if (ShouldSkipDirectory(sub))
                            continue;
                        stack.Push(sub);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (IOException)
                {
                }
            }
        }

        private static HashSet<string> BuildWantedDrawingKeys(IList<GeoQueryItem> queries)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (queries == null)
                return keys;

            foreach (var q in queries)
            {
                if (q == null || string.IsNullOrWhiteSpace(q.DrawingNumber))
                    continue;
                var key = OrderVerificationDrawingKey.Normalize(q.DrawingNumber);
                if (!string.IsNullOrWhiteSpace(key))
                    keys.Add(key);
            }

            return keys;
        }

        /// <summary>
        /// Schnellfilter: Dateiname muss einen Teil der gesuchten Zeichnungsnummer enthalten.
        /// </summary>
        private static List<string> BuildFilenameNeedles(IList<GeoQueryItem> queries)
        {
            var needles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (queries == null)
                return null;

            foreach (var q in queries)
            {
                if (q == null || string.IsNullOrWhiteSpace(q.DrawingNumber))
                    continue;

                var raw = q.DrawingNumber.Trim();
                if (raw.Length >= 5)
                    needles.Add(raw);

                var key = OrderVerificationDrawingKey.Normalize(raw);
                if (!string.IsNullOrWhiteSpace(key) && key.Length >= 5)
                    needles.Add(key);

                // TSL-1227-125 → 1227-125 (häufig im Dateinamen)
                var core = Regex.Match(raw, @"\d{3,8}-\d{2,8}(?:-[A-Z0-9]+)?", RegexOptions.IgnoreCase);
                if (core.Success && core.Value.Length >= 5)
                    needles.Add(core.Value);
            }

            if (needles.Count == 0)
                return null;

            // Längere Nadeln zuerst – weniger False Positives bei IndexOf-Schleife irrelevant,
            // aber kleinere Menge bleibt überschaubar.
            return needles.OrderByDescending(n => n.Length).ToList();
        }

        private static bool FileNameMightMatchNeedles(string fileName, IList<string> needles)
        {
            if (needles == null || needles.Count == 0)
                return true;
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            for (var i = 0; i < needles.Count; i++)
            {
                var needle = needles[i];
                if (needle.Length > 0
                    && fileName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool ShouldSkipDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;

            try
            {
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(name, "Anwendungsdaten", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Application Data", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Local Settings", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "AppData", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Cookies", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Recent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "SendTo", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Vorlagen", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Templates", StringComparison.OrdinalIgnoreCase))
                    return true;

                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch
            {
                return true;
            }

            return false;
        }

        public static bool MatchesQuery(GeoFileInfo geo, GeoQueryItem query)
        {
            if (geo == null || query == null)
                return false;

            if (!OrderVerificationDrawingKey.Equal(geo.DrawingNumber, query.DrawingNumber))
                return false;

            if (!string.IsNullOrWhiteSpace(query.DrawingRevision)
                && !string.IsNullOrWhiteSpace(geo.DrawingRevision)
                && !RevisionsCompatible(query.DrawingRevision, geo.DrawingRevision))
                return false;

            // Unterteil mit BG-Liste: wenn GEO eine BG trägt, muss sie passen
            if (query.BgNumbers != null && query.BgNumbers.Count > 0
                && !string.IsNullOrWhiteSpace(geo.BgNumber))
            {
                var anyBg = query.BgNumbers.Any(b => BgTokensCompatible(geo.BgNumber, geo.BgRevision, b));
                if (!anyBg)
                    return false;
            }

            return true;
        }

        private static bool BgTokensCompatible(string geoBg, string geoBgRev, string queryBgToken)
        {
            if (string.IsNullOrWhiteSpace(queryBgToken))
                return false;

            SplitDrawingAndRevision(queryBgToken, out var qDraw, out var qRev);
            if (string.IsNullOrWhiteSpace(qDraw))
                qDraw = queryBgToken.Trim();

            if (OrderVerificationDrawingKey.Equal(geoBg, qDraw))
            {
                if (!string.IsNullOrWhiteSpace(qRev) && !string.IsNullOrWhiteSpace(geoBgRev)
                    && !RevisionsCompatible(qRev, geoBgRev))
                    return false;
                return true;
            }

            var geoKey = OrderVerificationDrawingKey.Normalize(geoBg);
            var qKey = OrderVerificationDrawingKey.Normalize(qDraw);
            if (!string.IsNullOrWhiteSpace(geoKey) && !string.IsNullOrWhiteSpace(qKey)
                && string.Equals(geoKey, qKey, StringComparison.OrdinalIgnoreCase))
                return true;

            return geoBg.IndexOf(qDraw.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase) >= 0
                   || qDraw.IndexOf(geoBg.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<GeoCollectResult> CollectForOrder(
            IList<GeoQueryItem> queries,
            string orderNumber,
            IList<string> searchFolders = null,
            Action<string> progress = null)
        {
            var settings = GeoSettingsStore.Load();
            var folders = (searchFolders != null && searchFolders.Count > 0)
                ? searchFolders.ToList()
                : (settings.SearchFolders ?? new List<string>());

            progress?.Invoke("Scanne GEO-Ordner…");
            var geos = ScanFolders(folders, queries, progress);
            var targetFolder = GeoSettingsStore.ResolveTargetFolder(orderNumber);
            var results = new List<GeoCollectResult>();

            // Lookup nach normalisierter Zeichnungsnummer – O(n+m) statt O(n·m)
            var byDrawing = geos
                .GroupBy(g => OrderVerificationDrawingKey.Normalize(g.DrawingNumber) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            progress?.Invoke("Abgleich " + (queries?.Count ?? 0) + " Position(en)…");

            foreach (var query in queries ?? Array.Empty<GeoQueryItem>())
            {
                if (query == null || string.IsNullOrWhiteSpace(query.DrawingNumber))
                    continue;

                var qty = query.RequestedQuantity > 0
                    ? query.RequestedQuantity
                    : ParseRequestedQuantity(query.RequestedAmount);

                List<GeoFileInfo> candidates;
                var drawKey = OrderVerificationDrawingKey.Normalize(query.DrawingNumber);
                if (!string.IsNullOrWhiteSpace(drawKey)
                    && byDrawing.TryGetValue(drawKey, out var keyed)
                    && keyed != null)
                {
                    candidates = keyed;
                }
                else
                {
                    candidates = geos;
                }

                var matches = candidates.Where(g => MatchesQuery(g, query)).ToList();
                // Pro Zeichnung nur EINE GEO: passende Rev, bei Gleichstand neuestes Erstelldatum
                var match = SelectBestGeoMatch(matches, query);

                var bgText = FormatBgDisplay(query.BgNumbers);
                var thicknessHint = query.ThicknessMm ?? string.Empty;

                if (match == null)
                {
                    results.Add(new GeoCollectResult
                    {
                        DrawingNumber = query.DrawingNumber,
                        DrawingRevision = query.DrawingRevision,
                        BgNumbersText = bgText,
                        ThicknessMm = thicknessHint,
                        RequestedAmount = query.RequestedAmount,
                        RequestedQuantity = qty,
                        Status = "Muss neu erstellt werden",
                        Found = false,
                        ThicknessSortKey = BuildThicknessSortKey(thicknessHint)
                    });
                    continue;
                }

                var newName = ApplyQuantityToFileName(match.FileName, qty);
                var safeName = AppSecurityService.SanitizeFileName(newName);
                var dest = Path.Combine(targetFolder, safeName);
                dest = GetUniquePath(dest);

                var thickness = !string.IsNullOrWhiteSpace(match.ThicknessMm)
                    ? match.ThicknessMm
                    : thicknessHint;
                var revDisplay = !string.IsNullOrWhiteSpace(query.DrawingRevision)
                    ? query.DrawingRevision
                    : match.DrawingRevision;
                var bgDisplay = !string.IsNullOrWhiteSpace(match.BgNumber)
                    ? (match.BgNumber + (string.IsNullOrWhiteSpace(match.BgRevision) ? string.Empty : " " + match.BgRevision)).Trim()
                    : bgText;

                try
                {
                    File.Copy(match.FullPath, dest, overwrite: false);
                    results.Add(new GeoCollectResult
                    {
                        DrawingNumber = query.DrawingNumber,
                        DrawingRevision = revDisplay,
                        BgNumbersText = bgDisplay,
                        ThicknessMm = thickness,
                        RequestedAmount = query.RequestedAmount,
                        RequestedQuantity = qty,
                        Status = "OK",
                        Found = true,
                        SourcePath = match.FullPath,
                        CopyPath = dest,
                        FileName = Path.GetFileName(dest),
                        ThicknessSortKey = BuildThicknessSortKey(thickness)
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new GeoCollectResult
                    {
                        DrawingNumber = query.DrawingNumber,
                        DrawingRevision = revDisplay,
                        BgNumbersText = bgDisplay,
                        ThicknessMm = thickness,
                        RequestedAmount = query.RequestedAmount,
                        RequestedQuantity = qty,
                        Status = "Kopieren fehlgeschlagen: " + ex.Message,
                        Found = false,
                        SourcePath = match.FullPath,
                        FileName = match.FileName,
                        ThicknessSortKey = BuildThicknessSortKey(thickness)
                    });
                }
            }

            return results
                .OrderBy(r => r.ThicknessSortKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.DrawingNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<GeoQueryItem> BuildQueriesFromExcelRows(IEnumerable<OrderExcelRow> rows)
        {
            var list = new List<GeoQueryItem>();
            if (rows == null)
                return list;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.DrawingNumber))
                    continue;

                SplitDrawingAndRevision(row.DrawingNumber, out var drawing, out var revFromName);
                var rev = NormalizeRevision(
                    !string.IsNullOrWhiteSpace(row.RevisionNumber) ? row.RevisionNumber : revFromName);

                var bgs = new List<string>();
                if (row.BgDrawingNumbers != null)
                {
                    foreach (var b in row.BgDrawingNumbers)
                    {
                        if (!string.IsNullOrWhiteSpace(b)
                            && !bgs.Any(x => string.Equals(x, b.Trim(), StringComparison.OrdinalIgnoreCase)))
                            bgs.Add(b.Trim());
                    }
                }

                if (row.ParentArticleNumbers != null)
                {
                    foreach (var p in row.ParentArticleNumbers)
                    {
                        if (!string.IsNullOrWhiteSpace(p)
                            && !bgs.Any(x => string.Equals(x, p.Trim(), StringComparison.OrdinalIgnoreCase)))
                            bgs.Add(p.Trim());
                    }
                }

                if (!string.IsNullOrWhiteSpace(row.ParentArticleNumber)
                    && !bgs.Any(b => string.Equals(b, row.ParentArticleNumber, StringComparison.OrdinalIgnoreCase)))
                    bgs.Add(row.ParentArticleNumber.Trim());

                list.Add(new GeoQueryItem
                {
                    DrawingNumber = drawing,
                    DrawingRevision = rev,
                    BgNumbers = bgs,
                    ThicknessMm = ExtractThicknessToken(row.MaterialThicknessMm),
                    RequestedAmount = row.Amount ?? string.Empty,
                    RequestedQuantity = ParseRequestedQuantity(row.Amount)
                });
            }

            return list;
        }

        /// <summary>
        /// Liest Materialstärke aus sortierten Ablage-PDFs (.dok-meta.xml Wasserzeichen).
        /// </summary>
        public static void EnrichThicknessFromSortedPdfs(IList<GeoQueryItem> queries, string pdfFolder)
        {
            if (queries == null || queries.Count == 0)
                return;
            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
                return;

            string[] pdfs;
            try
            {
                pdfs = Directory.GetFiles(pdfFolder, "*.pdf", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return;
            }

            foreach (var pdf in pdfs)
            {
                var name = Path.GetFileName(pdf);
                if (string.IsNullOrWhiteSpace(name)
                    || name.StartsWith("_GESAMT_", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".prewm.pdf", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!WatermarkMetadataStore.TryLoadPlacements(pdf, out var metaDrawing, out _, out var placements))
                    continue;

                var thickness = ExtractThicknessFromPlacements(placements);
                if (string.IsNullOrWhiteSpace(thickness))
                    continue;

                foreach (var query in queries)
                {
                    if (query == null || !string.IsNullOrWhiteSpace(query.ThicknessMm))
                        continue;

                    var fromMeta = !string.IsNullOrWhiteSpace(metaDrawing) && OrderVerificationDrawingKey.Equal(metaDrawing, query.DrawingNumber);
                    var fromFileName = OrderVerificationDrawingKey.Equal(
                        Path.GetFileNameWithoutExtension(name), query.DrawingNumber)
                        || name.IndexOf(query.DrawingNumber.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase) >= 0;

                    if (fromMeta || fromFileName)
                        query.ThicknessMm = thickness;
                }
            }
        }

        public static void SplitDrawingAndRevision(string raw, out string drawing, out string revision)
        {
            drawing = (raw ?? string.Empty).Trim().ToUpperInvariant();
            revision = string.Empty;
            if (string.IsNullOrWhiteSpace(drawing))
                return;

            var m = Regex.Match(drawing, @"^(?<d>.+?)[\s_]+R(?<r>\d{1,3})$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                drawing = m.Groups["d"].Value.Trim().ToUpperInvariant();
                revision = "R" + m.Groups["r"].Value.PadLeft(2, '0');
                return;
            }

            // Anzeige: Originalzeichnung behalten (nicht normalisieren/strip)
            drawing = (raw ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string FormatBgDisplay(IList<string> bgs)
        {
            if (bgs == null || bgs.Count == 0)
                return string.Empty;
            return string.Join(", ", bgs.Where(b => !string.IsNullOrWhiteSpace(b)));
        }

        private static string ExtractThicknessFromPlacements(IEnumerable<WatermarkTextPlacement> placements)
        {
            if (placements == null)
                return string.Empty;

            foreach (var p in placements)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Text))
                    continue;
                if (!string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase)
                    && p.Text.IndexOf("mm", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var token = ExtractThicknessToken(p.Text);
                if (!string.IsNullOrWhiteSpace(token))
                    return token;
            }

            return string.Empty;
        }

        private static string ExtractThicknessToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            var m = ThicknessRegex.Match(text);
            if (!m.Success)
                return string.Empty;
            return m.Groups["mm"].Value.Replace(',', '.') + "mm";
        }

        /// <summary>
        /// Eine GEO pro Positionsnummer: richtige Revision bevorzugen/erzwingen,
        /// bei mehreren Treffern die Datei mit dem neuesten Erstelldatum.
        /// </summary>
        private static GeoFileInfo SelectBestGeoMatch(IList<GeoFileInfo> matches, GeoQueryItem query)
        {
            if (matches == null || matches.Count == 0)
                return null;

            IEnumerable<GeoFileInfo> pool = matches;

            if (!string.IsNullOrWhiteSpace(query?.DrawingRevision))
            {
                var withRev = matches
                    .Where(g => !string.IsNullOrWhiteSpace(g.DrawingRevision)
                                && RevisionsCompatible(query.DrawingRevision, g.DrawingRevision))
                    .ToList();

                // Falsche Rev nicht nehmen – lieber „neu erstellen“
                if (withRev.Count == 0)
                    return null;

                pool = withRev;
            }

            return pool
                .OrderByDescending(g => g.CreatedUtc)
                .ThenByDescending(g => g.LastWriteUtc)
                .ThenByDescending(g => string.IsNullOrWhiteSpace(g.BgNumber) ? 0 : 1)
                .ThenBy(g => g.FileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static bool RevisionsCompatible(string a, string b)
        {
            var na = NormalizeRevision(a);
            var nb = NormalizeRevision(b);
            if (string.IsNullOrWhiteSpace(na) || string.IsNullOrWhiteSpace(nb))
                return true;
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRevision(string rev)
        {
            if (string.IsNullOrWhiteSpace(rev))
                return string.Empty;
            var t = rev.Trim().ToUpperInvariant().Replace(" ", string.Empty);
            if (t.StartsWith("REV", StringComparison.Ordinal))
                t = "R" + t.Substring(3);
            if (!t.StartsWith("R", StringComparison.Ordinal))
                t = "R" + t;
            var digits = new string(t.Skip(1).TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0)
                return t;
            return "R" + digits.PadLeft(2, '0');
        }

        private static string BuildThicknessSortKey(string thicknessMm)
        {
            if (string.IsNullOrWhiteSpace(thicknessMm))
                return "zzz";
            var m = Regex.Match(thicknessMm, @"(\d+(?:[.,]\d+)?)");
            if (!m.Success)
                return thicknessMm;
            if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d.ToString("000.000", CultureInfo.InvariantCulture);
            return thicknessMm;
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var i = 2;
            while (true)
            {
                var candidate = Path.Combine(dir, name + "_" + i + ext);
                if (!File.Exists(candidate))
                    return candidate;
                i++;
            }
        }
    }
}
