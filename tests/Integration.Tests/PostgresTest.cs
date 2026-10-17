using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>Base for tests that need the database. Each test starts with empty tables, or skips when there is no server.</summary>
[Collection(PostgresCollection.Name)]
public abstract class PostgresTest(PostgresFixture fixture) : IAsyncLifetime
{
    private int _next;

    protected PostgresFixture Fixture => fixture;

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(fixture.Available, fixture.UnavailableReason);
        await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Runs <paramref name="action"/> in its own DI scope, as a request or a worker call would.</summary>
    protected async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected Task<bool> TryAddAsync(Receipt receipt) =>
        InScopeAsync(sp => sp.GetRequiredService<IReceiptRepository>().TryAddAsync(receipt, Token));

    /// <summary>Adds a Queued receipt created <paramref name="minutesOld"/> minutes ago, with a checksum of its own.</summary>
    protected async Task<Receipt> AddQueuedAsync(int minutesOld = 0, string? checksum = null)
    {
        var n = Interlocked.Increment(ref _next);
        var receipt = Receipt.Create(
            $"receipt-{n}.png", "image/png", 100, checksum ?? $"checksum-{Guid.NewGuid():N}", $"path-{n}.png",
            DateTimeOffset.UtcNow.AddMinutes(-minutesOld));
        Assert.True(await TryAddAsync(receipt));
        return receipt;
    }
}
