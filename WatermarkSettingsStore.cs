using System;
using System.IO;
using System.Xml.Linq;

namespace PDF_umbenenen
{
    public enum WatermarkDataPosition
    {
        LeftMiddleVertical,
        RightMiddleVertical,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public enum OrderNumberPosition
    {
        TopRight,
        TopLeft,
        BottomRight,
        BottomLeft
    }

    public sealed class WatermarkSettings
    {
        public bool WatermarksEnabled { get; set; } = true;

        public bool UseSmartPlacement { get; set; } = true;

        public bool ExtractMaterialType { get; set; } = true;

        public bool ExtractMaterialThickness { get; set; } = true;

        public WatermarkDataPosition DataPosition { get; set; } = WatermarkDataPosition.LeftMiddleVertical;

        public OrderNumberPosition OrderPosition { get; set; } = OrderNumberPosition.TopRight;

        public string DataPositionDisplayName => GetDataPositionLabel(DataPosition);

        public string OrderPositionDisplayName => GetOrderPositionLabel(OrderPosition);

        public static string GetDataPositionLabel(WatermarkDataPosition position)
        {
            switch (position)
            {
                case WatermarkDataPosition.RightMiddleVertical: return "Rechts Mitte (senkrecht)";
                case WatermarkDataPosition.TopLeft: return "Oben links";
                case WatermarkDataPosition.TopRight: return "Oben rechts";
                case WatermarkDataPosition.BottomLeft: return "Unten links";
                case WatermarkDataPosition.BottomRight: return "Unten rechts";
                default: return "Links Mitte (senkrecht)";
            }
        }

        public static string GetOrderPositionLabel(OrderNumberPosition position)
        {
            switch (position)
            {
                case OrderNumberPosition.TopLeft: return "Oben links";
                case OrderNumberPosition.BottomRight: return "Unten rechts";
                case OrderNumberPosition.BottomLeft: return "Unten links";
                default: return "Oben rechts";
            }
        }
    }

    internal static class WatermarkSettingsStore
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDF-umbenenen",
            "watermark-settings.xml");

        public static WatermarkSettings Load()
        {
            try
            {
                if (!File.Exists(StorePath))
                {
                    var defaults = new WatermarkSettings();
                    Save(defaults);
                    return defaults;
                }

                var doc = XDocument.Load(StorePath);
                var root = doc.Root;
                if (root == null)
                {
                    var defaults = new WatermarkSettings();
                    Save(defaults);
                    return defaults;
                }

                WatermarkDataPosition dataPosition;
                OrderNumberPosition orderPosition;
                var useSmartElement = root.Element("UseSmartPlacement");
                var useSmart = useSmartElement == null || !string.Equals((string)useSmartElement, "false", StringComparison.OrdinalIgnoreCase);
                var watermarksEnabledElement = root.Element("WatermarksEnabled");
                var watermarksEnabled = watermarksEnabledElement == null || !string.Equals((string)watermarksEnabledElement, "false", StringComparison.OrdinalIgnoreCase);
                var extractMaterialElement = root.Element("ExtractMaterialType");
                var extractMaterial = extractMaterialElement == null || !string.Equals((string)extractMaterialElement, "false", StringComparison.OrdinalIgnoreCase);
                var extractThicknessElement = root.Element("ExtractMaterialThickness");
                var extractThickness = extractThicknessElement == null || !string.Equals((string)extractThicknessElement, "false", StringComparison.OrdinalIgnoreCase);

                Enum.TryParse((string)root.Element("DataPosition"), true, out dataPosition);
                Enum.TryParse((string)root.Element("OrderPosition"), true, out orderPosition);

                var settings = new WatermarkSettings
                {
                    WatermarksEnabled = watermarksEnabled,
                    UseSmartPlacement = useSmart,
                    ExtractMaterialType = extractMaterial,
                    ExtractMaterialThickness = extractThickness,
                    DataPosition = Enum.IsDefined(typeof(WatermarkDataPosition), dataPosition)
                        ? dataPosition
                        : WatermarkDataPosition.LeftMiddleVertical,
                    OrderPosition = Enum.IsDefined(typeof(OrderNumberPosition), orderPosition)
                        ? orderPosition
                        : OrderNumberPosition.TopRight
                };

                return settings;
            }
            catch
            {
                return new WatermarkSettings();
            }
        }

        public static void Save(WatermarkSettings settings)
        {
            if (settings == null)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                new XDocument(
                    new XElement("WatermarkSettings",
                        new XElement("WatermarksEnabled", settings.WatermarksEnabled.ToString()),
                        new XElement("UseSmartPlacement", settings.UseSmartPlacement.ToString()),
                        new XElement("ExtractMaterialType", settings.ExtractMaterialType.ToString()),
                        new XElement("ExtractMaterialThickness", settings.ExtractMaterialThickness.ToString()),
                        new XElement("DataPosition", settings.DataPosition.ToString()),
                        new XElement("OrderPosition", settings.OrderPosition.ToString())
                    )
                ).Save(StorePath);
            }
            catch
            {
            }
        }
    }
}
