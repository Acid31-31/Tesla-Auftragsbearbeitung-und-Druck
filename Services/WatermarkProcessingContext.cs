using System;
using System.Collections.Generic;
using System.Linq;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Steuert schnellen Stapelmodus (kein Lernen pro Datei). OCR optional über Tesseract CLI.
    /// </summary>
    internal static class WatermarkProcessingContext
    {
        private static bool _batchMode;
        private static string _currentDrawingKey = string.Empty;
        private static HashSet<string> _assemblyDrawingKeys;
        private static DeliveryDatePosition? _deliveryDatePosition;

        public static bool BatchMode
        {
            get => _batchMode;
            set => _batchMode = value;
        }

        public static string CurrentDrawingKey
        {
            get => _currentDrawingKey ?? string.Empty;
            set => _currentDrawingKey = value ?? string.Empty;
        }

        /// <summary>
        /// Liefertermin-Position für den aktuellen Lauf (Batch/Editor).
        /// Falls nicht gesetzt → aus watermark-settings.xml.
        /// </summary>
        public static DeliveryDatePosition DeliveryDatePosition
        {
            get => _deliveryDatePosition
                   ?? WatermarkSettingsStore.Load().DeliveryDatePosition;
            set => _deliveryDatePosition = value;
        }

        public static void ClearDeliveryDatePositionOverride()
        {
            _deliveryDatePosition = null;
        }

        public static void SetAssemblyDrawingKeys(IEnumerable<string> drawingKeys)
        {
            if (drawingKeys == null)
            {
                _assemblyDrawingKeys = null;
                return;
            }

            _assemblyDrawingKeys = new HashSet<string>(
                drawingKeys
                    .Select(OrderVerificationDrawingKey.Normalize)
                    .Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);
        }

        public static void ClearAssemblyDrawingKeys()
        {
            _assemblyDrawingKeys = null;
        }

        public static bool IsAssemblyDrawingKey(string drawingKey)
        {
            if (_assemblyDrawingKeys == null || _assemblyDrawingKeys.Count == 0)
                return false;

            var normalized = OrderVerificationDrawingKey.Normalize(drawingKey);
            return !string.IsNullOrWhiteSpace(normalized)
                && _assemblyDrawingKeys.Contains(normalized);
        }
    }
}
