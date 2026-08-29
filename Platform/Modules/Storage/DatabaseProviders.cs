using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PDF_umbenenen.Platform.Contracts;

namespace PDF_umbenenen.Platform.Modules.Storage
{
    internal sealed class DatabaseProviderFactory : IDatabaseProviderFactory
    {
        public IDatabaseProvider Create(string providerName, string connectionString = null)
        {
            switch ((providerName ?? "sqlite").ToLowerInvariant())
            {
                case "sqlserver":
                case "mssql":
                    return new SqlServerDatabaseProvider(connectionString);
                case "postgresql":
                case "postgres":
                    return new PostgreSqlDatabaseProvider(connectionString);
                case "mysql":
                    return new MySqlDatabaseProvider(connectionString);
                default:
                    return new SqliteDatabaseProviderModule();
            }
        }
    }

    internal sealed class SqliteDatabaseProviderModule : IDatabaseProvider
    {
        private readonly string _dbPath;
        private readonly object _lock = new object();

        public SqliteDatabaseProviderModule()
        {
            _dbPath = Path.Combine(AppInfo.UserDataDirectory, "Data", "platform-store.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath));
            if (!File.Exists(_dbPath))
                new XDocument(new XElement("records")).Save(_dbPath);
        }

        public string ProviderName => "sqlite";

        public void Execute(string sql, IDictionary<string, object> parameters = null)
        {
            lock (_lock)
            {
                var doc = File.Exists(_dbPath) ? XDocument.Load(_dbPath) : new XDocument(new XElement("records"));
                var root = doc.Root ?? new XElement("records");
                root.Add(new XElement("record",
                    new XElement("sql", sql ?? string.Empty),
                    new XElement("utc", DateTime.UtcNow.ToString("o")),
                    new XElement("params", SerializeParams(parameters))));
                if (doc.Root == null)
                    doc.Add(root);
                doc.Save(_dbPath);
            }
        }

        public IList<IDictionary<string, object>> Query(string sql, IDictionary<string, object> parameters = null)
        {
            lock (_lock)
            {
                if (!File.Exists(_dbPath))
                    return new List<IDictionary<string, object>>();

                var doc = XDocument.Load(_dbPath);
                return doc.Root?.Elements("record")
                    .Select(el => (IDictionary<string, object>)new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sql"] = el.Element("sql")?.Value,
                        ["utc"] = el.Element("utc")?.Value,
                        ["params"] = el.Element("params")?.Value
                    })
                    .ToList() ?? new List<IDictionary<string, object>>();
            }
        }

        private static string SerializeParams(IDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            return string.Join(";", parameters.Select(p => p.Key + "=" + (p.Value ?? string.Empty)));
        }
    }

    internal sealed class SqlServerDatabaseProvider : IDatabaseProvider
    {
        private readonly string _connectionString;

        public SqlServerDatabaseProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string ProviderName => "sqlserver";

        public void Execute(string sql, IDictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return;

            using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
            using (var cmd = new System.Data.SqlClient.SqlCommand(sql, conn))
            {
                AddParameters(cmd, parameters);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public IList<IDictionary<string, object>> Query(string sql, IDictionary<string, object> parameters = null)
        {
            var rows = new List<IDictionary<string, object>>();
            if (string.IsNullOrWhiteSpace(_connectionString))
                return rows;

            using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
            using (var cmd = new System.Data.SqlClient.SqlCommand(sql, conn))
            {
                AddParameters(cmd, parameters);
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        for (var i = 0; i < reader.FieldCount; i++)
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        rows.Add(row);
                    }
                }
            }

            return rows;
        }

        private static void AddParameters(System.Data.SqlClient.SqlCommand cmd, IDictionary<string, object> parameters)
        {
            if (parameters == null)
                return;

            foreach (var pair in parameters)
                cmd.Parameters.AddWithValue("@" + pair.Key.TrimStart('@'), pair.Value ?? DBNull.Value);
        }
    }

    internal sealed class PostgreSqlDatabaseProvider : IDatabaseProvider
    {
        private readonly string _connectionString;

        public PostgreSqlDatabaseProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string ProviderName => "postgresql";

        public void Execute(string sql, IDictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new InvalidOperationException("PostgreSQL-Verbindung nicht konfiguriert (Npgsql-Plugin erforderlich).");
        }

        public IList<IDictionary<string, object>> Query(string sql, IDictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return new List<IDictionary<string, object>>();

            throw new InvalidOperationException("PostgreSQL-Abfrage nicht verfügbar ohne Npgsql-Provider.");
        }
    }

    internal sealed class MySqlDatabaseProvider : IDatabaseProvider
    {
        private readonly string _connectionString;

        public MySqlDatabaseProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string ProviderName => "mysql";

        public void Execute(string sql, IDictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new InvalidOperationException("MySQL-Verbindung nicht konfiguriert (MySqlConnector-Plugin erforderlich).");
        }

        public IList<IDictionary<string, object>> Query(string sql, IDictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return new List<IDictionary<string, object>>();

            throw new InvalidOperationException("MySQL-Abfrage nicht verfügbar ohne MySqlConnector-Provider.");
        }
    }
}
