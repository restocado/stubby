using System.Collections.Concurrent;
using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stubby.Application;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.RecoverInterruptedReceipts;
using Stubby.Domain.Receipts;
using Stubby.Infrastructure;
using Stubby.Web.Workers;

namespace Stubby.Integration.Tests;

/// <summary>The real worker, handlers, repository and database. Only the model is stubbed.</summary>
public class ExtractionWorkerTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly List<WorkerHarness> _harnesses = [];

    // Every worker is stopped when its test ends, so none keeps running against the shared database.
    public override async ValueTask DisposeAsync()
    {
        foreach (var harness in _harnesses)
        {
            await harness.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private WorkerHarness StartWorker(double pollSeconds = 0.1)
    {
        var harness = WorkerHarness.Create(Fixture, pollSeconds);
        _harnesses.Add(harness);
        return harness;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < Patience)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50, Token);
        }

        throw new TimeoutException($"Gave up waiting for {what}.");
    }

    private Task<bool> AllAsync(IEnumerable<Guid> ids, ReceiptStatus status) =>
        Task.FromResult(ids.ToList().All(id => Fixture.ReloadAsync(id).GetAwaiter().GetResult().Status == status));

    [Fact]
    public async Task Receipts_queued_before_start_are_processed_after_start()
    {
        var ids = new[] { await UploadAsync(), await UploadAsync(), await UploadAsync() };
        var worker = StartWorker();

        await worker.StartAsync(Token);
        await WaitUntilAsync(() => AllAsync(ids, ReceiptStatus.ForReview), "all three receipts to be processed");

        Assert.Equal(3, Fixture.Extractor.Calls);
    }

    [Fact]
    public async Task A_receipt_left_Processing_by_a_crash_is_requeued_with_its_attempts_unchanged_then_processed()
    {
        Fixture.Configuration["Extraction:MaxAttempts"] = "3";
        Fixture.Extractor.Failure = new ExtractionException("The call timed out.");
        var id = await UploadAsync();
        await InScopeAsync(async sp =>
        {
            // One failed attempt, then claimed again and never finished: the state a kill leaves behind.
            var repository = sp.GetRequiredService<IReceiptRepository>();
            var first = (await repository.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token))!;
            first.FailAttempt("failed once", 3, DateTimeOffset.UtcNow);
            await repository.SaveAsync(first, Token);
            return await repository.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token);
        });
        var interrupted = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.Processing, interrupted.Status);
        Assert.Equal(1, interrupted.AttemptsUsed);
        var worker = StartWorker();

        await worker.StartAsync(Token);
        await WaitUntilAsync(() => AllAsync([id], ReceiptStatus.Failed), "the receipt to use its attempts");

        // The attempt before the crash still counts: one before, plus two new ones, makes three. Had recovery
        // reset the count there would have been three new calls.
        Assert.Equal(2, Fixture.Extractor.Calls);
        Assert.Equal(3, (await Fixture.ReloadAsync(id)).AttemptsUsed);
        Assert.Contains(worker.Logs.Lines, line => line.Contains("Returned 1 interrupted receipts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recovery_returns_every_Processing_receipt_to_Queued_and_leaves_the_rest()
    {
        var interrupted = await UploadAsync();
        var completed = await UploadAsync();
        var stillQueued = await UploadAsync();
        var repository = new Func<Func<IReceiptRepository, Task<Receipt?>>, Task<Receipt?>>(
            action => InScopeAsync(sp => action(sp.GetRequiredService<IReceiptRepository>())));
        // Uploads are claimed oldest first: the first is left Processing, the second is completed, the third stays Queued.
        Assert.Equal(interrupted, (await repository(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token)))!.Id);
        var second = (await repository(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token)))!;
        Assert.Equal(completed, second.Id);
        second.CompleteExtraction("Cafe", new DateOnly(2026, 10, 2), 5m, "m", "v1", false, null, DateTimeOffset.UtcNow);
        await repository(async r =>
        {
            await r.SaveAsync(second, Token);
            return null;
        });

        var count = await InScopeAsync(async sp =>
            await sp.GetRequiredService<IMediator>().Send(new RecoverInterruptedReceiptsCommand(), Token));

        Assert.Equal(1, count);
        Assert.Equal(ReceiptStatus.Queued, (await Fixture.ReloadAsync(interrupted)).Status);
        Assert.Equal(ReceiptStatus.ForReview, (await Fixture.ReloadAsync(completed)).Status);
        Assert.Equal(ReceiptStatus.Queued, (await Fixture.ReloadAsync(stillQueued)).Status);
    }

    [Fact]
    public async Task An_upload_while_the_worker_is_busy_returns_at_once_and_is_picked_up_next()
    {
        Fixture.Extractor.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = await UploadAsync();
        var worker = StartWorker();
        await worker.StartAsync(Token);
        await WaitUntilAsync(() => Task.FromResult(Fixture.Extractor.Calls == 1), "the first extraction to start");

        var timer = Stopwatch.StartNew();
        var second = await UploadAsync();
        var uploadTook = timer.Elapsed;

        Assert.True(uploadTook < TimeSpan.FromSeconds(3), $"The upload took {uploadTook} while the worker was busy.");
        Assert.Equal(ReceiptStatus.Processing, (await Fixture.ReloadAsync(first)).Status);
        Assert.Equal(ReceiptStatus.Queued, (await Fixture.ReloadAsync(second)).Status);

        Fixture.Extractor.Gate.SetResult();
        await WaitUntilAsync(() => AllAsync([first, second], ReceiptStatus.ForReview), "both receipts to be processed");
        Assert.Equal(2, Fixture.Extractor.Calls);
    }

    [Fact]
    public async Task An_empty_queue_makes_the_worker_wait_and_not_spin()
    {
        var worker = StartWorker(pollSeconds: 0.2);

        await worker.StartAsync(Token);
        await Task.Delay(TimeSpan.FromSeconds(1.5), Token);
        await worker.StopAsync(Token);

        var claims = worker.Logs.Lines.Count(line => line.Contains("FOR UPDATE SKIP LOCKED", StringComparison.Ordinal));
        Assert.InRange(claims, 3, 12);
    }

    [Fact]
    public async Task An_exception_for_one_call_does_not_stop_the_loop()
    {
        var worker = StartWorker();
        await worker.StartAsync(Token);

        // The worker started with valid settings. Making one invalid now makes every call throw before a receipt
        // is claimed, as an unexpected fault would.
        Fixture.Configuration["Extraction:MaxAttempts"] = "0";
        var id = await UploadAsync();
        await WaitUntilAsync(
            () => Task.FromResult(worker.Logs.Lines.Count(l => l.Contains("Processing the next receipt failed", StringComparison.Ordinal)) >= 3),
            "the worker to fail and carry on");
        Assert.Equal(ReceiptStatus.Queued, (await Fixture.ReloadAsync(id)).Status);
        Assert.False(worker.Worker.ExecuteTask!.IsCompleted);

        Fixture.Configuration["Extraction:MaxAttempts"] = "1";
        await WaitUntilAsync(() => AllAsync([id], ReceiptStatus.ForReview), "the receipt to be processed once the fault is fixed");
    }

    [Fact]
    public async Task Stopping_mid_extraction_requeues_without_charging_an_attempt_and_it_is_processed_once_after_restart()
    {
        Fixture.Extractor.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = await UploadAsync();
        var first = StartWorker();
        await first.StartAsync(Token);
        await WaitUntilAsync(() => Task.FromResult(Fixture.Extractor.Calls == 1), "the extraction to start");

        await first.StopAsync(Token);

        var stopped = await Fixture.ReloadAsync(id);
        Assert.Equal(ReceiptStatus.Queued, stopped.Status);
        Assert.Equal(0, stopped.AttemptsUsed);

        Fixture.Extractor.Gate.SetResult();
        var second = StartWorker();
        await second.StartAsync(Token);
        await WaitUntilAsync(() => AllAsync([id], ReceiptStatus.ForReview), "the receipt to be processed after the restart");
        Assert.Equal(2, Fixture.Extractor.Calls);
        Assert.Equal(0, (await Fixture.ReloadAsync(id)).AttemptsUsed);
    }

    [Fact]
    public async Task With_the_provider_down_receipts_end_Failed_and_the_worker_keeps_running()
    {
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        var ids = new[] { await UploadAsync(), await UploadAsync(), await UploadAsync() };
        var worker = StartWorker();

        await worker.StartAsync(Token);
        await WaitUntilAsync(() => AllAsync(ids, ReceiptStatus.Failed), "all receipts to fail");

        Assert.False(worker.Worker.ExecuteTask!.IsCompleted);
        foreach (var id in ids)
        {
            var receipt = await Fixture.ReloadAsync(id);
            Assert.Equal(1, receipt.AttemptsUsed);
            Assert.Equal("The provider returned HTTP 503.", receipt.LastError);
        }

        Assert.DoesNotContain(worker.Logs.Lines, line => line.Contains("[Error]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receipts_failed_while_the_provider_was_down_are_processed_again_after_they_are_requeued()
    {
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        var id = await UploadAsync();
        var worker = StartWorker();
        await worker.StartAsync(Token);
        await WaitUntilAsync(() => AllAsync([id], ReceiptStatus.Failed), "the receipt to fail");

        // What "reprocess" will do, until that slice exists.
        Fixture.Extractor.Failure = null;
        var failed = await Fixture.ReloadAsync(id);
        failed.Reprocess(DateTimeOffset.UtcNow);
        await InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IReceiptRepository>().SaveAsync(failed, Token);
            return true;
        });

        await WaitUntilAsync(() => AllAsync([id], ReceiptStatus.ForReview), "the requeued receipt to be processed");
    }
}

internal sealed class WorkerHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private WorkerHarness(ServiceProvider provider, ExtractionWorker worker, CapturingLogs logs)
    {
        _provider = provider;
        Worker = worker;
        Logs = logs;
    }

    public ExtractionWorker Worker { get; }

    public CapturingLogs Logs { get; }

    public static WorkerHarness Create(PostgresFixture fixture, double pollSeconds)
    {
        var logs = new CapturingLogs();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new CapturingLogProvider(logs)));
        services.AddApplication();
        services.AddInfrastructure(fixture.Configuration);
        services.AddSingleton<IReceiptExtractor>(fixture.Extractor);
        var provider = services.BuildServiceProvider();
        var worker = new ExtractionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ExtractionWorkerOptions { PollInterval = TimeSpan.FromSeconds(pollSeconds) },
            provider.GetRequiredService<ILogger<ExtractionWorker>>());
        return new WorkerHarness(provider, worker, logs);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Worker.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Worker.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Worker.StopAsync(CancellationToken.None);
        Worker.Dispose();
        await _provider.DisposeAsync();
    }
}

internal sealed class CapturingLogs
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public void Add(string line) => _lines.Enqueue(line);
}

internal sealed class CapturingLogProvider(CapturingLogs logs) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, logs);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, CapturingLogs logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logs.Add($"{category} [{logLevel}] {formatter(state, exception)}");
    }
}
