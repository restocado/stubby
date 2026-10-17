using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Receipts.UploadReceipt;

namespace Stubby.Application.Tests.Receipts;

public class UploadReceiptWiringTests
{
    [Fact]
    public async Task Command_sent_through_the_mediator_reaches_the_handler()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IReceiptRepository>(new FakeReceiptRepository());
        services.AddSingleton<IImageStore>(new FakeImageStore());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var response = await mediator.Send(
            new UploadReceiptCommand("receipt.png", "image/png", content.Length, new MemoryStream(content)),
            TestContext.Current.CancellationToken);

        Assert.IsType<UploadReceiptResponse.Created>(response);
    }
}
