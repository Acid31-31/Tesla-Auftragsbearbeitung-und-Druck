using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace PDF_umbenenen.Services
{
    internal static class OutlookAttachmentDropService
    {
        private const int FileDescriptorWSize = 592;
        private const int FileDescriptorASize = 332;
        private const int MaxVirtualFiles = 200;
        private static readonly string[] SupportedExtensions = { ".pdf", ".xlsx", ".xls", ".zip", ".msg" };

        public static bool CanAccept(IDataObject data)
        {
            if (data == null)
                return false;

            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop, true) && HasUsableFileDrop(data))
                    return true;

                var formats = data.GetFormats(true) ?? Array.Empty<string>();
                return formats.Any(IsOutlookOrVirtualFormat);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetDroppedFiles(IDataObject data, out List<string> filePaths, out string sourceHint)
        {
            filePaths = new List<string>();
            sourceHint = string.Empty;
            if (data == null)
                return false;

            var usableDrop = GetUsableFileDrop(data);
            if (usableDrop.Count > 0)
            {
                filePaths.AddRange(ExpandMessageFiles(usableDrop));
                if (usableDrop.Any(p => IsMsg(p)))
                    sourceHint = "Outlook-Nachricht";
                return filePaths.Count > 0;
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), "DOK-V01-OutlookDrop", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var virtualFiles = ExtractVirtualFiles(data, tempRoot);
                if (virtualFiles.Count > 0)
                {
                    filePaths.AddRange(ExpandMessageFiles(virtualFiles));
                    sourceHint = "Outlook";
                    return filePaths.Count > 0;
                }

                var fromMail = ExtractFromOutlookSelection(tempRoot);
                if (fromMail.Count > 0)
                {
                    filePaths.AddRange(fromMail);
                    sourceHint = "Outlook-E-Mail";
                    return true;
                }
            }
            catch (Exception ex)
            {
                sourceHint = "Outlook (" + ex.Message + ")";
            }

            return false;
        }

        public static void TryCleanupTempFiles(IEnumerable<string> paths)
        {
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                try
                {
                    var full = Path.GetFullPath(path);
                    if (full.IndexOf("DOK-V01-OutlookDrop", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (File.Exists(full))
                        File.Delete(full);
                }
                catch
                {
                    // Temp darf liegen bleiben.
                }
            }
        }

        private static bool HasUsableFileDrop(IDataObject data)
        {
            return GetUsableFileDrop(data).Count > 0;
        }

        private static List<string> GetUsableFileDrop(IDataObject data)
        {
            var result = new List<string>();
            try
            {
                if (!data.GetDataPresent(DataFormats.FileDrop, true))
                    return result;

                var files = data.GetData(DataFormats.FileDrop, true) as string[];
                if (files == null)
                    return result;

                foreach (var path in files)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        continue;
                    if (File.Exists(path) || Directory.Exists(path))
                        result.Add(path);
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool IsOutlookOrVirtualFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
                return false;

            return format.IndexOf("FileGroupDescriptor", StringComparison.OrdinalIgnoreCase) >= 0
                || format.Equals("FileContents", StringComparison.OrdinalIgnoreCase)
                || format.IndexOf("RenPrivate", StringComparison.OrdinalIgnoreCase) >= 0
                || format.IndexOf("Outlook", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> ExtractVirtualFiles(IDataObject data, string tempRoot)
        {
            var names = ReadFileNames(data);
            var saved = new List<string>();
            if (names.Count == 0)
                return saved;

            var ole = TryGetComDataObject(data);
            for (var i = 0; i < names.Count; i++)
            {
                var name = AppSecurityService.SanitizeFileName(names[i]);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var ext = Path.GetExtension(name)?.ToLowerInvariant() ?? string.Empty;
                if (!SupportedExtensions.Contains(ext))
                    continue;

                byte[] bytes = null;
                if (ole != null)
                    bytes = TryReadFileContents(ole, i);

                if (bytes == null && i == 0)
                    bytes = TryReadFirstFileContents(data);

                if (bytes == null || bytes.Length == 0)
                    continue;

                var target = GetUniquePath(tempRoot, name);
                File.WriteAllBytes(target, bytes);
                saved.Add(target);
            }

            if (saved.Count == 0 && names.Any(n => IsMsg(n)))
                saved.AddRange(ExtractFromOutlookSelection(tempRoot));

            return saved;
        }

        private static List<string> ReadFileNames(IDataObject data)
        {
            var names = ReadFileDescriptorNames(data, "FileGroupDescriptorW", Encoding.Unicode, FileDescriptorWSize, 520);
            if (names.Count == 0)
                names = ReadFileDescriptorNames(data, "FileGroupDescriptor", Encoding.Default, FileDescriptorASize, 260);
            return names;
        }

        private static List<string> ReadFileDescriptorNames(
            IDataObject data,
            string format,
            Encoding encoding,
            int descriptorSize,
            int nameByteLength)
        {
            var names = new List<string>();
            try
            {
                if (!data.GetDataPresent(format, true))
                    return names;

                var raw = data.GetData(format, true);
                var stream = raw as MemoryStream;
                if (stream == null && raw is Stream other)
                {
                    stream = new MemoryStream();
                    other.CopyTo(stream);
                }

                if (stream == null)
                    return names;

                var bytes = stream.ToArray();
                if (bytes.Length < 8)
                    return names;

                var count = BitConverter.ToInt32(bytes, 0);
                if (count <= 0 || count > MaxVirtualFiles)
                    return names;

                var offset = 4;
                for (var i = 0; i < count && offset + descriptorSize <= bytes.Length; i++)
                {
                    var nameBytes = new byte[nameByteLength];
                    Buffer.BlockCopy(bytes, offset + 72, nameBytes, 0, nameByteLength);
                    var name = encoding.GetString(nameBytes).TrimEnd('\0').Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(Path.GetFileName(name));
                    offset += descriptorSize;
                }
            }
            catch
            {
            }

            return names;
        }

        private static byte[] TryReadFirstFileContents(IDataObject data)
        {
            try
            {
                if (!data.GetDataPresent("FileContents", true))
                    return null;

                var raw = data.GetData("FileContents", true);
                if (raw is MemoryStream ms)
                    return ms.ToArray();
                if (raw is Stream stream)
                {
                    using (var copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        return copy.ToArray();
                    }
                }

                if (raw is byte[] bytes)
                    return bytes;
            }
            catch
            {
            }

            return null;
        }

        private static ComTypes.IDataObject TryGetComDataObject(IDataObject data, int depth = 0)
        {
            if (data == null || depth > 4)
                return null;

            if (data is ComTypes.IDataObject ole)
                return ole;

            try
            {
                var type = data.GetType();
                var flags = System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic;
                foreach (var name in new[] { "InnerData", "_innerData", "oleData", "_oleData" })
                {
                    var prop = type.GetProperty(name, flags);
                    var value = prop?.GetValue(data, null);
                    if (value is ComTypes.IDataObject fromProp)
                        return fromProp;

                    var field = type.GetField(name, flags);
                    value = field?.GetValue(data);
                    if (value is ComTypes.IDataObject fromField)
                        return fromField;

                    if (value != null && !ReferenceEquals(value, data) && value is IDataObject nestedWpf)
                    {
                        var nested = TryGetComDataObject(nestedWpf, depth + 1);
                        if (nested != null)
                            return nested;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static byte[] TryReadFileContents(ComTypes.IDataObject ole, int index)
        {
            var formatId = RegisterClipboardFormat("FileContents");
            if (formatId == 0)
                return null;

            var formatEtc = new ComTypes.FORMATETC
            {
                cfFormat = unchecked((short)formatId),
                dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = index,
                ptd = IntPtr.Zero,
                tymed = ComTypes.TYMED.TYMED_ISTREAM | ComTypes.TYMED.TYMED_HGLOBAL
            };

            ComTypes.STGMEDIUM medium;
            try
            {
                ole.GetData(ref formatEtc, out medium);
            }
            catch
            {
                return null;
            }

            try
            {
                if (medium.tymed == ComTypes.TYMED.TYMED_ISTREAM && medium.unionmember != IntPtr.Zero)
                {
                    var stream = Marshal.GetObjectForIUnknown(medium.unionmember) as ComTypes.IStream;
                    return ReadComStream(stream);
                }

                if (medium.tymed == ComTypes.TYMED.TYMED_HGLOBAL && medium.unionmember != IntPtr.Zero)
                    return ReadHGlobal(medium.unionmember);
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }

            return null;
        }

        private static byte[] ReadComStream(ComTypes.IStream stream)
        {
            if (stream == null)
                return null;

            try
            {
                stream.Stat(out var stat, 1);
                var size = stat.cbSize;
                if (size <= 0 || size > 512L * 1024 * 1024)
                    return CopyStreamUnknownSize(stream);

                var buffer = new byte[size];
                var remaining = (int)size;
                var offset = 0;
                var readPtr = Marshal.AllocHGlobal(8);
                try
                {
                    while (remaining > 0)
                    {
                        var chunk = Math.Min(remaining, 64 * 1024);
                        var slice = new byte[chunk];
                        stream.Read(slice, chunk, readPtr);
                        var read = Marshal.ReadInt32(readPtr);
                        if (read <= 0)
                            break;
                        Buffer.BlockCopy(slice, 0, buffer, offset, read);
                        offset += read;
                        remaining -= read;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(readPtr);
                }

                if (offset == buffer.Length)
                    return buffer;

                var trimmed = new byte[offset];
                Buffer.BlockCopy(buffer, 0, trimmed, 0, offset);
                return trimmed;
            }
            catch
            {
                return CopyStreamUnknownSize(stream);
            }
        }

        private static byte[] CopyStreamUnknownSize(ComTypes.IStream stream)
        {
            if (stream == null)
                return null;

            using (var ms = new MemoryStream())
            {
                var chunk = new byte[64 * 1024];
                var readPtr = Marshal.AllocHGlobal(8);
                try
                {
                    while (true)
                    {
                        stream.Read(chunk, chunk.Length, readPtr);
                        var read = Marshal.ReadInt32(readPtr);
                        if (read <= 0)
                            break;
                        ms.Write(chunk, 0, read);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(readPtr);
                }

                return ms.ToArray();
            }
        }

        private static byte[] ReadHGlobal(IntPtr hglobal)
        {
            var size = GlobalSize(hglobal).ToInt64();
            if (size <= 0 || size > 512L * 1024 * 1024)
                return null;

            var locked = GlobalLock(hglobal);
            if (locked == IntPtr.Zero)
                return null;

            try
            {
                var bytes = new byte[size];
                Marshal.Copy(locked, bytes, 0, (int)size);
                return bytes;
            }
            finally
            {
                GlobalUnlock(hglobal);
            }
        }

        private static List<string> ExpandMessageFiles(IEnumerable<string> paths)
        {
            var result = new List<string>();
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (!IsMsg(path))
                {
                    result.Add(path);
                    continue;
                }

                var folder = Path.Combine(
                    Path.GetDirectoryName(path) ?? Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(path) + "_att");
                Directory.CreateDirectory(folder);
                var extracted = ExtractMsgAttachments(path, folder);
                if (extracted.Count > 0)
                    result.AddRange(extracted);
                else
                    result.AddRange(ExtractFromOutlookSelection(folder));
            }

            return result;
        }

        private static List<string> ExtractFromOutlookSelection(string targetFolder)
        {
            var saved = new List<string>();
            object app = null;
            object session = null;
            object explorer = null;
            object selection = null;
            try
            {
                app = GetOutlookApplication();
                if (app == null)
                    return saved;

                session = app.GetType().InvokeMember("Session", System.Reflection.BindingFlags.GetProperty, null, app, null);
                explorer = app.GetType().InvokeMember("ActiveExplorer", System.Reflection.BindingFlags.InvokeMethod, null, app, null);
                if (explorer == null)
                    return saved;

                selection = explorer.GetType().InvokeMember("Selection", System.Reflection.BindingFlags.GetProperty, null, explorer, null);
                if (selection == null)
                    return saved;

                var count = Convert.ToInt32(selection.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, selection, null));
                for (var i = 1; i <= count; i++)
                {
                    object item = null;
                    try
                    {
                        item = selection.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, selection, new object[] { i });
                        saved.AddRange(SaveMailAttachments(item, targetFolder));
                    }
                    finally
                    {
                        ReleaseCom(item);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseCom(selection);
                ReleaseCom(explorer);
                ReleaseCom(session);
                ReleaseCom(app);
            }

            return saved.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> ExtractMsgAttachments(string msgPath, string targetFolder)
        {
            var saved = new List<string>();
            if (string.IsNullOrWhiteSpace(msgPath) || !File.Exists(msgPath))
                return saved;

            object app = null;
            object session = null;
            object mail = null;
            try
            {
                app = GetOutlookApplication();
                if (app == null)
                    return saved;

                session = app.GetType().InvokeMember("Session", System.Reflection.BindingFlags.GetProperty, null, app, null);
                if (session == null)
                    return saved;

                mail = session.GetType().InvokeMember(
                    "OpenSharedItem",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    session,
                    new object[] { msgPath });
                saved.AddRange(SaveMailAttachments(mail, targetFolder));
            }
            catch
            {
            }
            finally
            {
                try
                {
                    mail?.GetType().InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, mail, new object[] { 1 });
                }
                catch
                {
                }

                ReleaseCom(mail);
                ReleaseCom(session);
                ReleaseCom(app);
            }

            return saved;
        }

        private static List<string> SaveMailAttachments(object mailItem, string targetFolder)
        {
            var saved = new List<string>();
            if (mailItem == null)
                return saved;

            object attachments = null;
            try
            {
                attachments = mailItem.GetType().InvokeMember("Attachments", System.Reflection.BindingFlags.GetProperty, null, mailItem, null);
                if (attachments == null)
                    return saved;

                var count = Convert.ToInt32(attachments.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, attachments, null));
                for (var i = 1; i <= count; i++)
                {
                    object attachment = null;
                    try
                    {
                        attachment = attachments.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, attachments, new object[] { i });
                        var fileName = Convert.ToString(attachment.GetType().InvokeMember("FileName", System.Reflection.BindingFlags.GetProperty, null, attachment, null));
                        var type = 0;
                        try
                        {
                            type = Convert.ToInt32(attachment.GetType().InvokeMember("Type", System.Reflection.BindingFlags.GetProperty, null, attachment, null));
                        }
                        catch
                        {
                            type = 1;
                        }

                        if (type == 6)
                            continue;

                        var safeName = AppSecurityService.SanitizeFileName(fileName);
                        var ext = Path.GetExtension(safeName)?.ToLowerInvariant() ?? string.Empty;
                        if (!SupportedExtensions.Contains(ext))
                            continue;

                        var target = GetUniquePath(targetFolder, safeName);
                        attachment.GetType().InvokeMember("SaveAsFile", System.Reflection.BindingFlags.InvokeMethod, null, attachment, new object[] { target });
                        if (File.Exists(target))
                        {
                            if (IsMsg(target))
                                saved.AddRange(ExtractMsgAttachments(target, targetFolder));
                            else
                                saved.Add(target);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        ReleaseCom(attachment);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseCom(attachments);
            }

            return saved;
        }

        private static object GetOutlookApplication()
        {
            try
            {
                return Marshal.GetActiveObject("Outlook.Application");
            }
            catch
            {
            }

            try
            {
                var type = Type.GetTypeFromProgID("Outlook.Application");
                return type == null ? null : Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        private static void ReleaseCom(object com)
        {
            if (com == null)
                return;

            try
            {
                Marshal.ReleaseComObject(com);
            }
            catch
            {
            }
        }

        private static bool IsMsg(string path)
        {
            return string.Equals(Path.GetExtension(path), ".msg", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetUniquePath(string folder, string fileName)
        {
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, fileName);
            if (!File.Exists(target))
                return target;

            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (var i = 2; i < 1000; i++)
            {
                var candidate = Path.Combine(folder, name + "_" + i + ext);
                if (!File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(folder, Guid.NewGuid().ToString("N") + ext);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalSize(IntPtr hMem);

        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM pmedium);
    }
}
