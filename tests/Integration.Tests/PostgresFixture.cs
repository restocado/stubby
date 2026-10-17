using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Stubby.Application;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;
using Stubby.Infrastructure;
using Stubby.Infrastructure.Persistence;

namespace Stubby.Integration.Tests;

/// <summary>
/// A real PostgreSQL database that exists only for this test run. It connects with <c>STUBBY_TEST_PG</c>
/// (a connection string that may create databases) or <c>postgres/postgres</c> on localhost, creates a database
/// named <c>stubby_it_...</c>, migrates it, and drops it at the end. When no server is reachable the tests skip.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultAdminConnection = "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    private string _adminConnection = DefaultAdminConnection;
    private string _databaseName = string.Empty;
    private string _imageRoot = string.Empty;

    public bool Available { get; private set; }

    public string UnavailableReason { get; private set; } = string.Empty;

    public ServiceProvider Provider { get; private set; } = null!;

    public IConfigurationRoot Configuration { get; private set; } = null!;

    internal StubExtractor Extractor { get; } = new();

    public string ImageRoot => _imageRoot;

    public async ValueTask InitializeAsync()
    {
        _adminConnection = Environment.GetEnvironmentVariable("STUBBY_TEST_PG") ?? DefaultAdminConnection;
        _databaseName = "stubby_it_" + Guid.NewGuid().ToString("N");
        _imageRoot = Path.Combine(Path.GetTempPath(), _databaseName);

        try
        {
            await using var admin = new NpgsqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            UnavailableReason = $"No PostgreSQL server could be used for integration tests ({ex.GetType().Name}). "
                + "Set STUBBY_TEST_PG to a connection string that can create databases.";
            return;
        }

        var testConnection = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName }.ConnectionString;
        Configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "web-appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Stubby"] = testConnection,
                ["ImageStorage:RootPath"] = _imageRoot,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddInfrastructure(Configuration);
        services.AddSingleton<IReceiptExtractor>(Extractor);
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await Provider.MigrateDatabaseAsync();
        Available = true;
    }

    /// <summary>Empties both tables and restarts the transaction numbers, so each test starts clean.</summary>
    public async Task ResetAsync()
    {
        var factory = Provider.GetRequiredService<IDbContextFactory<StubbyDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE \"Receipts\", \"FieldRecords\" RESTART IDENTITY");
        Extractor.Reset();
        Configuration["Extraction:MaxAttempts"] = "1";
        Configuration["Extraction:FlagTotalLimit"] = "3000";
        if (Directory.Exists(_imageRoot))
        {
            Directory.Delete(_imageRoot, recursive: true);
        }
    }

    public async Task<T> WithDbAsync<T>(Func<StubbyDbContext, Task<T>> action)
    {
        var factory = Provider.GetRequiredService<IDbContextFactory<StubbyDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await action(db);
    }

    public async Task<Receipt> ReloadAsync(Guid id) =>
        await WithDbAsync(db => db.Receipts.AsNoTracking().SingleAsync(r => r.Id == id));

    public async ValueTask DisposeAsync()
    {
        if (Provider is not null)
        {
            await Provider.DisposeAsync();
        }

        if (string.IsNullOrEmpty(_databaseName))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        try
        {
            await using var admin = new NpgsqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException)
        {
            // Nothing was created if the server was never reachable.
        }

        if (Directory.Exists(_imageRoot))
        {
            Directory.Delete(_imageRoot, recursive: true);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>An extractor whose answer a test sets.</summary>
internal sealed class StubExtractor : IReceiptExtractor
{
    public static ExtractionResult Sample { get; } = new(
        "Fake Cafe", new DateOnly(2026, 10, 2), 12.50m, Subtotal: 11.50m, Tax: 1.00m, "stub-model:1b", "v1");

    public ExtractionResult Result { get; set; } = Sample;

    public Exception? Failure { get; set; }

    public Action<CancellationToken>? OnExtract { get; set; }

    /// <summary>When set, every call waits for it, so a test can hold a receipt mid-extraction. A shutdown cancels the wait.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public int Calls => _calls;

    private int _calls;

    public void Reset()
    {
        Result = Sample;
        Failure = null;
        OnExtract = null;
        Gate = null;
        _calls = 0;
    }

    public async Task<ExtractionResult> ExtractAsync(ReceiptFile file, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        OnExtract?.Invoke(cancellationToken);
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return Failure is null ? Result : throw Failure;
    }
}
