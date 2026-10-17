using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;
using Stubby.Infrastructure.AiGateway;
using Stubby.Infrastructure.Persistence;
using Stubby.Infrastructure.Storage;

namespace Stubby.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var imageRoot = configuration["ImageStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(imageRoot))
        {
            throw new InvalidOperationException("Setting 'ImageStorage:RootPath' is required.");
        }

        services.AddDbContextFactory<StubbyDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Stubby")));
        services.AddScoped<IReceiptRepository, ReceiptRepository>();
        services.AddSingleton<IImageStore>(sp =>
            new FileSystemImageStore(imageRoot, sp.GetRequiredService<ILogger<FileSystemImageStore>>()));
        services.AddAiGateway(configuration);
        return services;
    }
}
