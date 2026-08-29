using System.Collections.Generic;

namespace PDF_umbenenen.Models
{
    internal sealed class LearnedCategoryDefault
    {
        public string Category { get; set; } = string.Empty;

        public float RelativeX { get; set; }

        public float RelativeY { get; set; }

        public int Alignment { get; set; }

        public float Angle { get; set; }

        public float FontSize { get; set; } = 22f;

        public bool UseRedText { get; set; }
    }

    internal sealed class LearnedWatermarkPlacement
    {
        public string Category { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public float X { get; set; }

        public float Y { get; set; }

        /// <summary>0..1 relativ zur Seitenbreite; wenn &gt; 0, beim Anwenden bevorzugt.</summary>
        public float RelativeX { get; set; }

        /// <summary>0..1 relativ zur Seitenhöhe; wenn &gt; 0, beim Anwenden bevorzugt.</summary>
        public float RelativeY { get; set; }

        public int Alignment { get; set; }

        public float Angle { get; set; }

        public float FontSize { get; set; } = 22f;

        public bool UseRedText { get; set; }
    }

    internal sealed class WatermarkLearningEntry
    {
        public string DrawingKey { get; set; } = string.Empty;

        public string CustomerProfileId { get; set; } = string.Empty;

        public List<LearnedWatermarkPlacement> Placements { get; set; } = new List<LearnedWatermarkPlacement>();

        public string MaterialOverride { get; set; } = string.Empty;
    }

    internal sealed class WatermarkEditorItem
    {
        public string PdfPath { get; set; } = string.Empty;

        public string PreWatermarkPath { get; set; } = string.Empty;

        public string DrawingNumber { get; set; } = string.Empty;

        public string EncodedWatermarks { get; set; } = string.Empty;

        public float PageWidth { get; set; }

        public float PageHeight { get; set; }
    }
}
