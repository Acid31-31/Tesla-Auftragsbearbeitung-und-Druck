using System;
using System.Collections.Generic;

namespace PDF_umbenenen.Platform.Models
{
    public sealed class VisionLayoutResult
    {
        public bool HasTitleBlock { get; set; }

        public bool HasTable { get; set; }

        public bool HasStamp { get; set; }

        public bool HasLogo { get; set; }

        public bool HasSignature { get; set; }

        public bool HasTechnicalSymbols { get; set; }

        public IList<string> Dimensions { get; set; } = new List<string>();

        public IList<string> PositionNumbers { get; set; } = new List<string>();

        public double Confidence { get; set; }

        public IDictionary<string, string> RawFeatures { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ResourceSnapshot
    {
        public long WorkingSetMb { get; set; }

        public int ProcessorCount { get; set; }

        public double CpuUsagePercent { get; set; }

        public bool GpuAvailable { get; set; }

        public string GpuName { get; set; }

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class PlatformUser
    {
        public string UserId { get; set; } = Guid.NewGuid().ToString("N");

        public string UserName { get; set; }

        public string DisplayName { get; set; }

        public string RoleId { get; set; } = "operator";

        public bool IsAuthenticated { get; set; }
    }

    public sealed class PlatformRole
    {
        public string RoleId { get; set; }

        public string DisplayName { get; set; }

        public IList<string> Permissions { get; set; } = new List<string>();
    }

    public enum DatabaseProviderKind
    {
        Sqlite,
        SqlServer,
        PostgreSql,
        MySql
    }

    public sealed class PlatformSettingsSnapshot
    {
        public bool AiEnabled { get; set; }

        public bool LocalOcrEnabled { get; set; }

        public string TesseractPath { get; set; }

        public bool LocalVisionEnabled { get; set; }

        public DatabaseProviderKind DatabaseProvider { get; set; } = DatabaseProviderKind.Sqlite;

        public string DatabaseConnectionString { get; set; }

        public int ApiPort { get; set; } = 8765;

        public bool ApiEnabled { get; set; }
    }
}
