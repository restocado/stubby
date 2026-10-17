using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application;
using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.Tests;

public class InfrastructureWiringTests
{
    [Fact]
    public async Task Services_from_the_shipped_settings_all_resolve()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "web-appsettings.json"))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // ValidateOnBuild checks that every registered service can be built, without opening a database connection.
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReceiptExtractor>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAiGateway>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPdfRenderer>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IImageStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReceiptRepository>());
        Assert.Equal("gemma4:31b", scope.ServiceProvider.GetRequiredService<IAiGateway>().Model);
    }
}
