using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Support
{
    /// <summary>
    /// Runs only when a Docker engine is reachable; otherwise the test is reported as skipped.
    /// Set SKIP_DOCKER_TESTS=true to skip explicitly (e.g. on CI agents without Docker).
    /// </summary>
    public sealed class DockerFactAttribute : FactAttribute
    {
        public DockerFactAttribute()
        {
            if (!IsDockerAvailable())
            {
                Skip = "Docker is not available; SQL Server integration test skipped.";
            }
        }

        internal static bool IsDockerAvailable()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("SKIP_DOCKER_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST"))
                || File.Exists("/var/run/docker.sock")
                || (OperatingSystem.IsWindows() && File.Exists(@"\\.\pipe\docker_engine"));
        }
    }

    [CollectionDefinition(Name)]
    public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
    {
        public const string Name = "SqlServer";
    }

    /// <summary>
    /// One disposable SQL Server container per test run, initialized from the real <c>schema.sql</c>
    /// and the forward migrations (which must be idempotent on a fresh schema).
    /// Tests isolate their data by using a unique TenantId.
    /// </summary>
    public sealed partial class SqlServerFixture : IAsyncLifetime
    {
        /// <summary>Pinned image: tests must not change behavior when "latest" moves.</summary>
        public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

        private MsSqlContainer? _container;

        public string ConnectionString { get; private set; } = string.Empty;

        public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();

        public async Task InitializeAsync()
        {
            if (!DockerFactAttribute.IsDockerAvailable())
            {
                return; // every test in the collection is skipped
            }

            _container = new MsSqlBuilder(SqlServerImage).Build();
            await _container.StartAsync();

            await using (var master = new SqlConnection(_container.GetConnectionString()))
            {
                await master.OpenAsync();
                foreach (var script in new[] { "schema.sql", "scripts/003-fintech-ledger.sql", "scripts/004-hitl-serialized-context.sql", "scripts/005-hitl-tenant-scoped-key.sql" })
                {
                    await ExecuteScriptAsync(master, script);
                }
            }

            ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
            {
                InitialCatalog = "AgenticPoC"
            }.ConnectionString;

            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AgenticDatabase"] = ConnectionString })
                .Build();
        }

        public async Task DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }

        public async Task<SqlConnection> OpenAsync()
        {
            var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task SeedAccountAsync(string tenantId, string accountId, string currency, decimal balance, bool frozen = false)
        {
            await using var connection = await OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO dbo.Accounts (TenantId, AccountId, Currency, Balance, IsFrozen) VALUES (@tenantId, @accountId, @currency, @balance, @frozen);",
                new { tenantId, accountId, currency, balance, frozen });
        }

        public async Task<decimal> GetBalanceAsync(string tenantId, string accountId)
        {
            await using var connection = await OpenAsync();
            return await connection.ExecuteScalarAsync<decimal>(
                "SELECT Balance FROM dbo.Accounts WHERE TenantId = @tenantId AND AccountId = @accountId;",
                new { tenantId, accountId });
        }

        /// <summary>
        /// Creates <paramref name="database"/> and applies <paramref name="scripts"/> to it in order
        /// (the scripts' <c>USE AgenticPoC</c> is redirected), returning its connection string.
        /// Used to test the upgrade path of an existing, non-fresh database.
        /// </summary>
        public async Task<string> CreateDatabaseFromScriptsAsync(string database, params string[] scripts)
        {
            await using (var master = new SqlConnection(_container!.GetConnectionString()))
            {
                await master.OpenAsync();
                await master.ExecuteAsync($"CREATE DATABASE [{database}];");
                foreach (var script in scripts)
                {
                    await ExecuteScriptAsync(master, script, database);
                }
            }

            return new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database }.ConnectionString;
        }

        private static async Task ExecuteScriptAsync(SqlConnection connection, string relativePath, string? targetDatabase = null)
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), relativePath));
            if (targetDatabase is not null)
            {
                sql = sql.Replace("USE AgenticPoC;", $"USE [{targetDatabase}];", StringComparison.Ordinal);
            }

            foreach (var batch in GoSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await connection.ExecuteAsync(batch);
            }
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgenticWorkflowPoC.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }

        [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
        private static partial Regex GoSeparator();
    }
}
