using System;
using System.Collections.Generic;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Material-Platzierung über <see cref="MaterialFreeAreaPlacementService"/> (vereinfacht, seitenweit).
    /// </summary>
    internal static class ReliableMaterialPlacementService
    {
        public static List<WatermarkTextPlacement> Place(
            IList<string> lines,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupiedRects,
            MaterialPlacementPageLog pageLog = null,
            IList<MaterialBlockBounds> forbiddenZones = null)
        {
            return MaterialFreeAreaPlacementService.Place(
                lines,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                occupiedRects,
                pageLog,
                forbiddenZones);
        }
    }
}
