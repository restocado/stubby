using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Receipts.UploadReceipt;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>Base for tests that need the database. Each test starts with empty tables, or skips when there is no server.</summary>
[Collection(PostgresCollection.Name)]
public abstract class PostgresTest(PostgresFixture fixture) : IAsyncLifetime
{
    private int _next;
    private int _uploads;

    protected PostgresFixture Fixture => fixture;

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(fixture.Available, fixture.UnavailableReason);
        await fixture.ResetAsync();
    }

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Runs <paramref name="action"/> in its own DI scope, as a request or a worker call would.</summary>
    protected async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected Task<bool> TryAddAsync(Receipt receipt) =>
        InScopeAsync(sp => sp.GetRequiredService<IReceiptRepository>().TryAddAsync(receipt, Token));

    /// <summary>Uploads a small PNG with bytes of its own through the real upload handler. Returns the new receipt's id.</summary>
    protected Task<Guid> UploadAsync() =>
        UploadAsync(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, (byte)Interlocked.Increment(ref _uploads)],
            "receipt.png",
            "image/png");

    protected async Task<Guid> UploadAsync(byte[] content, string fileName, string contentType)
    {
        var response = await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new UploadReceiptCommand(fileName, contentType, content.Length, new MemoryStream(content)), Token));
        return Assert.IsType<UploadReceiptResponse.Created>(response).Id;
    }

    /// <summary>Runs the worker's step once, as the background worker would.</summary>
    protected Task<ProcessNextReceiptResult> ProcessOneAsync() =>
        InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(new ProcessNextReceiptCommand(), Token));

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
