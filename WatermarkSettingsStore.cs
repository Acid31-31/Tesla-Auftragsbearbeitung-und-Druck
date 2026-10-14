using System;
using System.IO;
using System.Xml.Linq;
using PDF_umbenenen.Services;

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

    public enum DeliveryDatePosition
    {
        TopLeft,
        TopRight
    }

    /// <summary>Platzierung von Material (Art/Stärke) auf der Zeichnung.</summary>
    public enum MaterialThicknessPlacement
    {
        /// <summary>Freie Fläche (Raster/KI).</summary>
        Free,

        /// <summary>Fest unten links, gleiche Baseline wie Seitenzahl.</summary>
        BottomLeftFixed
    }

    /// <summary>Platzierung der Baugruppen-Nummern (BG).</summary>
    public enum BgPlacement
    {
        /// <summary>Freie Fläche (bisher unten links stapeln).</summary>
        Free,

        /// <summary>Fest rechts, senkrecht (−90°), Schrift wie Seitenzahl.</summary>
        RightVerticalFixed
    }

    public sealed class WatermarkSettings
    {
        public bool WatermarksEnabled { get; set; } = true;

        public bool UseSmartPlacement { get; set; } = true;

        public bool ExtractMaterialType { get; set; } = true;

        public bool ExtractMaterialThickness { get; set; } = true;

        /// <summary>Materialstärke/-art: fest unten links (Höhe wie Seitenzahl). Free nur noch Legacy.</summary>
        public MaterialThicknessPlacement MaterialThicknessPlacement { get; set; } =
            MaterialThicknessPlacement.BottomLeftFixed;

        /// <summary>Baugruppen-Wasserzeichen ein-/ausschalten (Position immer fest rechts senkrecht).</summary>
        public bool BgWatermarkEnabled { get; set; } = true;

        /// <summary>Baugruppen: fest rechts senkrecht. Free nur noch Legacy.</summary>
        public BgPlacement BgPlacement { get; set; } = BgPlacement.RightVerticalFixed;

        /// <summary>Pulverbeschichtung (RAL) aus Oberflächenbehandlung.</summary>
        public bool ExtractPowderCoating { get; set; } = false;

        /// <summary>True: RAL mit Nummer (z. B. RAL 9003), false: nur „RAL“.</summary>
        public bool PowderCoatingShowRalNumber { get; set; } = true;

        /// <summary>Eloxierung aus Oberflächenbehandlung als „ELOX“.</summary>
        public bool ExtractAnodizing { get; set; } = false;

        public WatermarkDataPosition DataPosition { get; set; } = WatermarkDataPosition.LeftMiddleVertical;

        public OrderNumberPosition OrderPosition { get; set; } = OrderNumberPosition.TopRight;

        /// <summary>Liefertermin/Menge oben links oder oben rechts am Zeichnungsrahmen.</summary>
        public DeliveryDatePosition DeliveryDatePosition { get; set; } = DeliveryDatePosition.TopLeft;

        /// <summary>
        /// Unten pro Zeichnung am oberen Rand spiegeln (Maßstab-unabhängig).
        /// </summary>
        public bool MirrorBottomToTopBand { get; set; } = true;

        public string DataPositionDisplayName => GetDataPositionLabel(DataPosition);

        public string OrderPositionDisplayName => GetOrderPositionLabel(OrderPosition);

        public string DeliveryDatePositionDisplayName => GetDeliveryDatePositionLabel(DeliveryDatePosition);

        public string MaterialThicknessPlacementDisplayName =>
            GetMaterialThicknessPlacementLabel(MaterialThicknessPlacement);

        public string BgPlacementDisplayName => GetBgPlacementLabel(BgPlacement);

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

        public static string GetDeliveryDatePositionLabel(DeliveryDatePosition position)
        {
            return position == DeliveryDatePosition.TopRight
                ? "Oben rechts"
                : "Oben links";
        }

        public static string GetMaterialThicknessPlacementLabel(MaterialThicknessPlacement placement)
        {
            return placement == MaterialThicknessPlacement.BottomLeftFixed
                ? "Unten links (wie Seitenzahl)"
                : "Frei platzierbar";
        }

        public static string GetBgPlacementLabel(BgPlacement placement)
        {
            return placement == BgPlacement.RightVerticalFixed
                ? "Rechts senkrecht (−90°)"
                : "Frei platzierbar";
        }
    }

    internal static class WatermarkSettingsStore
    {
        private static string StorePath => UserSettingsPath.GetFilePath("watermark-settings.xml");

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
                DeliveryDatePosition deliveryDatePosition;
                var useSmartElement = root.Element("UseSmartPlacement");
                var storedSmartOff = useSmartElement != null
                    && string.Equals((string)useSmartElement, "false", StringComparison.OrdinalIgnoreCase);
                var watermarksEnabledElement = root.Element("WatermarksEnabled");
                var watermarksEnabled = watermarksEnabledElement == null || !string.Equals((string)watermarksEnabledElement, "false", StringComparison.OrdinalIgnoreCase);
                var extractMaterialElement = root.Element("ExtractMaterialType");
                var extractMaterial = extractMaterialElement == null || !string.Equals((string)extractMaterialElement, "false", StringComparison.OrdinalIgnoreCase);
                var extractThicknessElement = root.Element("ExtractMaterialThickness");
                var extractThickness = extractThicknessElement == null || !string.Equals((string)extractThicknessElement, "false", StringComparison.OrdinalIgnoreCase);
                var extractPowderElement = root.Element("ExtractPowderCoating");
                var extractPowder = extractPowderElement != null && string.Equals((string)extractPowderElement, "true", StringComparison.OrdinalIgnoreCase);
                var powderRalNumberElement = root.Element("PowderCoatingShowRalNumber");
                var powderRalNumber = powderRalNumberElement == null || !string.Equals((string)powderRalNumberElement, "false", StringComparison.OrdinalIgnoreCase);
                var extractAnodizingElement = root.Element("ExtractAnodizing");
                var extractAnodizing = extractAnodizingElement != null && string.Equals((string)extractAnodizingElement, "true", StringComparison.OrdinalIgnoreCase);
                MaterialThicknessPlacement materialThicknessPlacement;
                if (!Enum.TryParse((string)root.Element("MaterialThicknessPlacement"), true, out materialThicknessPlacement))
                    materialThicknessPlacement = MaterialThicknessPlacement.BottomLeftFixed;
                // Freie Platzierung entfällt – immer feste Position.
                if (materialThicknessPlacement == MaterialThicknessPlacement.Free)
                    materialThicknessPlacement = MaterialThicknessPlacement.BottomLeftFixed;

                BgPlacement bgPlacement;
                if (!Enum.TryParse((string)root.Element("BgPlacement"), true, out bgPlacement))
                    bgPlacement = BgPlacement.RightVerticalFixed;
                if (bgPlacement == BgPlacement.Free)
                    bgPlacement = BgPlacement.RightVerticalFixed;

                var bgEnabledElement = root.Element("BgWatermarkEnabled");
                var bgWatermarkEnabled = bgEnabledElement == null
                    || !string.Equals((string)bgEnabledElement, "false", StringComparison.OrdinalIgnoreCase);

                Enum.TryParse((string)root.Element("DataPosition"), true, out dataPosition);
                Enum.TryParse((string)root.Element("OrderPosition"), true, out orderPosition);
                if (!Enum.TryParse((string)root.Element("DeliveryDatePosition"), true, out deliveryDatePosition))
                    deliveryDatePosition = DeliveryDatePosition.TopLeft;

                var mirrorBottomElement = root.Element("MirrorBottomToTopBand");
                // Default an; nur explizit "false" schaltet ab. Altes BottomBandOffsetMm wird ignoriert.
                var mirrorBottom = mirrorBottomElement == null
                    || !string.Equals((string)mirrorBottomElement, "false", StringComparison.OrdinalIgnoreCase);

                var settings = new WatermarkSettings
                {
                    WatermarksEnabled = watermarksEnabled,
                    UseSmartPlacement = true,
                    ExtractMaterialType = extractMaterial,
                    ExtractMaterialThickness = extractThickness,
                    MaterialThicknessPlacement = Enum.IsDefined(typeof(MaterialThicknessPlacement), materialThicknessPlacement)
                        ? materialThicknessPlacement
                        : MaterialThicknessPlacement.BottomLeftFixed,
                    BgWatermarkEnabled = bgWatermarkEnabled,
                    BgPlacement = Enum.IsDefined(typeof(BgPlacement), bgPlacement)
                        ? bgPlacement
                        : BgPlacement.RightVerticalFixed,
                    ExtractPowderCoating = extractPowder,
                    PowderCoatingShowRalNumber = powderRalNumber,
                    ExtractAnodizing = extractAnodizing,
                    DataPosition = Enum.IsDefined(typeof(WatermarkDataPosition), dataPosition)
                        ? dataPosition
                        : WatermarkDataPosition.LeftMiddleVertical,
                    OrderPosition = Enum.IsDefined(typeof(OrderNumberPosition), orderPosition)
                        ? orderPosition
                        : OrderNumberPosition.TopRight,
                    DeliveryDatePosition = Enum.IsDefined(typeof(DeliveryDatePosition), deliveryDatePosition)
                        ? deliveryDatePosition
                        : DeliveryDatePosition.TopLeft,
                    MirrorBottomToTopBand = mirrorBottom
                };

                // Intelligente Platzierung ist Pflicht. Alte XML (Update lässt die Datei liegen)
                // oder Kunden-Schnappschuss mit False wird beim Laden korrigiert.
                if (storedSmartOff)
                    Save(settings);

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
                settings.UseSmartPlacement = true;
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                new XDocument(
                    new XElement("WatermarkSettings",
                        new XElement("WatermarksEnabled", settings.WatermarksEnabled.ToString()),
                        new XElement("UseSmartPlacement", "True"),
                        new XElement("ExtractMaterialType", settings.ExtractMaterialType.ToString()),
                        new XElement("ExtractMaterialThickness", settings.ExtractMaterialThickness.ToString()),
                        new XElement("MaterialThicknessPlacement", settings.MaterialThicknessPlacement.ToString()),
                        new XElement("BgWatermarkEnabled", settings.BgWatermarkEnabled.ToString()),
                        new XElement("BgPlacement", settings.BgPlacement.ToString()),
                        new XElement("ExtractPowderCoating", settings.ExtractPowderCoating.ToString()),
                        new XElement("PowderCoatingShowRalNumber", settings.PowderCoatingShowRalNumber.ToString()),
                        new XElement("ExtractAnodizing", settings.ExtractAnodizing.ToString()),
                        new XElement("DataPosition", settings.DataPosition.ToString()),
                        new XElement("OrderPosition", settings.OrderPosition.ToString()),
                        new XElement("DeliveryDatePosition", settings.DeliveryDatePosition.ToString()),
                        new XElement("MirrorBottomToTopBand", settings.MirrorBottomToTopBand.ToString())
                    )
                ).Save(StorePath);
            }
            catch
            {
            }
        }
    }
}
