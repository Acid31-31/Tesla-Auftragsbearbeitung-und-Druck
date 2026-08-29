using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Stellt sicher, dass native DLLs (pdfium) zur Prozess-Architektur passen.
    /// Behebt HRESULT 0x8007000B nach Updates mit falscher pdfium-Version.
    /// </summary>
    internal static class NativeDependencyService
    {
        private const ushort PeMachineAmd64 = 0x8664;
        private const ushort PeMachineI386 = 0x014c;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        public static bool TryEnsurePdfium(out string message, string appDirectory = null)
        {
            message = string.Empty;
            var baseDir = string.IsNullOrWhiteSpace(appDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : appDirectory;

            var pdfiumPath = Path.Combine(baseDir, "pdfium.dll");
            if (!File.Exists(pdfiumPath))
            {
                if (!TryRepairPdfium(baseDir))
                {
                    message = "pdfium.dll fehlt im App-Ordner.";
                    return false;
                }
            }

            if (!File.Exists(pdfiumPath))
            {
                message = "pdfium.dll fehlt im App-Ordner.";
                return false;
            }

            if (!IsDllCompatibleWithProcess(pdfiumPath))
            {
                if (!TryRepairPdfium(baseDir))
                {
                    message = Environment.Is64BitProcess
                        ? "pdfium.dll ist 32-Bit, die Anwendung läuft als 64-Bit. Bitte DOK-V01 aktualisieren oder neu installieren."
                        : "pdfium.dll passt nicht zur Anwendungs-Architektur. Bitte DOK-V01 neu installieren.";
                    return false;
                }

                if (!IsDllCompatibleWithProcess(pdfiumPath))
                {
                    message = "pdfium.dll konnte nicht repariert werden. Bitte DOK-V01 vollständig neu installieren.";
                    return false;
                }
            }

            if (!TryLoadNativeLibrary(pdfiumPath, out var loadError))
            {
                message = loadError;
                return false;
            }

            return true;
        }

        public static bool IsDllCompatibleWithProcess(string dllPath)
        {
            if (!TryGetPeMachineType(dllPath, out var machine))
                return true;

            if (Environment.Is64BitProcess)
                return machine == PeMachineAmd64;

            return machine == PeMachineI386;
        }

        public static bool TryRepairPdfium(string appDirectory)
        {
            if (string.IsNullOrWhiteSpace(appDirectory))
                return false;

            var runtime = Environment.Is64BitProcess ? "win-x64" : "win-x86";
            var backup = Path.Combine(appDirectory, "native", runtime, "pdfium.dll");
            var target = Path.Combine(appDirectory, "pdfium.dll");
            if (!File.Exists(backup))
                return false;

            try
            {
                File.Copy(backup, target, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsBadImageFormatError(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is BadImageFormatException)
                return true;

            var message = ex.Message ?? string.Empty;
            return message.IndexOf("0x8007000B", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("falsches Format", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("incorrect format", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string FormatBadImageMessage()
        {
            return "Die PDF-Vorschau konnte nicht geladen werden, weil pdfium.dll nicht zur Anwendung passt (32/64-Bit).\n\n"
                + "Bitte über „Update prüfen“ auf die neueste Version aktualisieren oder DOK-V01 neu installieren.\n"
                + "Falls das Problem bleibt: App schließen, Ordner "
                + AppInfo.GetInstallDirectory()
                + " löschen und neu installieren.";
        }

        public static string FormatMissingPdfiumMessage(string technicalDetail = null)
        {
            var detail = string.IsNullOrWhiteSpace(technicalDetail)
                ? string.Empty
                : "\n\nTechnisch: " + technicalDetail.Trim();

            return "Die eingebaute PDF-Vorschau ist nicht verfügbar (pdfium fehlt oder ist beschädigt)."
                + detail
                + "\n\nSo beheben:\n"
                + "1. Alle DOK-V01-Fenster schließen\n"
                + "2. Menü → Update prüfen → neueste Version installieren\n"
                + "3. Falls nötig: DOK-V01 deinstallieren und neu installieren\n\n"
                + "Alternativ: unten „Gesamt-PDF öffnen“ — dann im PDF-Reader anseigen.";
        }

        public static bool TryRepairAndReload(out string message)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            TryRepairPdfium(baseDir);
            PdfRasterLayoutService.ResetAvailability();
            return TryEnsurePdfium(out message);
        }

        private static bool TryLoadNativeLibrary(string dllPath, out string error)
        {
            error = string.Empty;
            var handle = LoadLibrary(dllPath);
            if (handle != IntPtr.Zero)
            {
                FreeLibrary(handle);
                return true;
            }

            var win32 = Marshal.GetLastWin32Error();
            if (win32 == 193)
            {
                error = Environment.Is64BitProcess
                    ? "pdfium.dll ist nicht 64-Bit."
                    : "pdfium.dll ist nicht 32-Bit.";
                return false;
            }

            error = "pdfium.dll konnte nicht geladen werden (Win32-Fehler " + win32 + ").";
            return false;
        }

        private static bool TryGetPeMachineType(string filePath, out ushort machine)
        {
            machine = 0;
            try
            {
                using (var stream = File.OpenRead(filePath))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 0x40)
                        return false;

                    if (reader.ReadUInt16() != 0x5A4D)
                        return false;

                    stream.Seek(0x3C, SeekOrigin.Begin);
                    var peOffset = reader.ReadInt32();
                    if (peOffset <= 0 || peOffset + 6 > stream.Length)
                        return false;

                    stream.Seek(peOffset, SeekOrigin.Begin);
                    if (reader.ReadUInt32() != 0x00004550)
                        return false;

                    machine = reader.ReadUInt16();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
