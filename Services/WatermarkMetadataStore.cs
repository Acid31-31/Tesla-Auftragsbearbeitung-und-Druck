using System;
using System.IO;
using System.Xml.Linq;

namespace PDF_umbenenen.Services
{
    internal static class WatermarkMetadataStore
    {
        public static string GetMetaPath(string pdfPath)
        {
            return pdfPath + ".dok-meta.xml";
        }

        public static void Save(string pdfPath, string drawingNumber, string encodedWatermarks)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                return;

            var doc = new XDocument(
                new XElement("WatermarkMeta",
                    new XElement("DrawingNumber", drawingNumber ?? string.Empty),
                    new XElement("EncodedWatermarks", encodedWatermarks ?? string.Empty),
                    new XElement("SavedUtc", DateTime.UtcNow.ToString("o"))));

            doc.Save(GetMetaPath(pdfPath));
        }

        public static bool TryLoad(string pdfPath, out string drawingNumber, out string encodedWatermarks)
        {
            drawingNumber = string.Empty;
            encodedWatermarks = string.Empty;

            var metaPath = GetMetaPath(pdfPath);
            if (!File.Exists(metaPath))
                return false;

            try
            {
                var root = XDocument.Load(metaPath).Root;
                if (root == null)
                    return false;

                drawingNumber = root.Element("DrawingNumber")?.Value ?? string.Empty;
                encodedWatermarks = root.Element("EncodedWatermarks")?.Value ?? string.Empty;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
