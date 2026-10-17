using System.Reflection;
using ArchUnitNET.Loader;

namespace Stubby.Architecture.Tests;

internal static class StubbyArchitecture
{
    public const string DomainAssembly = "Stubby.Domain";
    public const string ApplicationAssembly = "Stubby.Application";
    public const string InfrastructureAssembly = "Stubby.Infrastructure";
    public const string WebAssembly = "Stubby.Web";
    public const string EvalAssembly = "Stubby.Eval";

    public static readonly ArchUnitNET.Domain.Architecture Instance = new ArchLoader()
        .LoadAssemblies(
            Assembly.Load(DomainAssembly),
            Assembly.Load(ApplicationAssembly),
            Assembly.Load(InfrastructureAssembly),
            Assembly.Load(WebAssembly),
            Assembly.Load(EvalAssembly))
        .Build();
}
