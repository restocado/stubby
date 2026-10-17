using Mediator;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Extraction.RecoverInterruptedReceipts;

namespace Stubby.Web.Workers;

/// <summary>
/// Reads receipts in the background. One instance only: it takes the oldest Queued receipt, again and again,
/// and waits when there is none. It does not pause while the AI gateway's circuit is open: receipts use
/// their attempts and become Failed, and the user reprocesses them later (NFR-R3).
/// </summary>
public sealed class ExtractionWorker(
    IServiceScopeFactory scopes,
    ExtractionWorkerOptions options,
    ILogger<ExtractionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedReceiptsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedOne = false;
            try
            {
                var result = await SendAsync(new ProcessNextReceiptCommand(), stoppingToken);
                if (result is ProcessNextReceiptResult.Interrupted)
                {
                    break;
                }

                // Run again at once while there is work. Wait only when the queue is empty.
                processedOne = result is not ProcessNextReceiptResult.NothingToDo;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad call must not stop the loop. The wait below keeps a persistent fault, such as a
                // database outage, from turning into a tight loop.
                logger.LogError(ex, "Processing the next receipt failed. The worker carries on.");
            }

            if (!processedOne && !await WaitAsync(stoppingToken))
            {
                break;
            }
        }
    }

    // Receipts left Processing by a crash or a kill go back to Queued before anything is processed (NFR-R2).
    // If the database is not ready, it tries again until it works or the app stops.
    private async Task RecoverInterruptedReceiptsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendAsync(new RecoverInterruptedReceiptsCommand(), stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Returning interrupted receipts to the queue failed. Trying again.");
                if (!await WaitAsync(stoppingToken))
                {
                    return;
                }
            }
        }
    }

    // A scope for every call, because handlers may use scoped services.
    private async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command, cancellationToken);
    }

    /// <summary>Waits for the poll interval. Returns false when the app is stopping.</summary>
    private async Task<bool> WaitAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(options.PollInterval, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
