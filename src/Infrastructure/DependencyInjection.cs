using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;
using Stubby.Infrastructure.AiGateway;
using Stubby.Infrastructure.Extraction;
using Stubby.Infrastructure.Pdf;
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

        var pdfDpi = configuration.GetValue("Pdf:Dpi", PdfToImageRenderer.DefaultDpi);
        if (pdfDpi is < PdfToImageRenderer.MinDpi or > PdfToImageRenderer.MaxDpi)
        {
            throw new InvalidOperationException(
                $"Setting 'Pdf:Dpi' must be between {PdfToImageRenderer.MinDpi} and {PdfToImageRenderer.MaxDpi}.");
        }

        services.AddSingleton<IPdfRenderer>(new PdfToImageRenderer(pdfDpi));
        services.AddTransient<IReceiptExtractor, VisionReceiptExtractor>();

        var extractionSettings = new ConfigurationExtractionSettings(configuration);
        extractionSettings.Validate();
        services.AddSingleton<IExtractionSettings>(extractionSettings);
        return services;
    }
}
