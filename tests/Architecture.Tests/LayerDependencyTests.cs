using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Stubby.Architecture.Tests.StubbyArchitecture;

namespace Stubby.Architecture.Tests;

public class LayerDependencyTests
{
    [Fact]
    public void Domain_depends_on_no_other_project()
    {
        Types().That().ResideInAssembly(DomainAssembly)
            .Should().NotDependOnAnyTypesThat().ResideInAssembly(ApplicationAssembly)
            .AndShould().NotDependOnAnyTypesThat().ResideInAssembly(InfrastructureAssembly)
            .AndShould().NotDependOnAnyTypesThat().ResideInAssembly(WebAssembly)
            .AndShould().NotDependOnAnyTypesThat().ResideInAssembly(EvalAssembly)
            .WithoutRequiringPositiveResults()
            .Check(Instance);
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        Types().That().ResideInAssembly(ApplicationAssembly)
            .Should().NotDependOnAnyTypesThat().ResideInAssembly(InfrastructureAssembly)
            .AndShould().NotDependOnAnyTypesThat().ResideInAssembly(WebAssembly)
            .AndShould().NotDependOnAnyTypesThat().ResideInAssembly(EvalAssembly)
            .WithoutRequiringPositiveResults()
            .Check(Instance);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Web()
    {
        Types().That().ResideInAssembly(InfrastructureAssembly)
            .Should().NotDependOnAnyTypesThat().ResideInAssembly(WebAssembly)
            .AndShould().NotDependOnAnyTypesThat().ResideInAssembly(EvalAssembly)
            .WithoutRequiringPositiveResults()
            .Check(Instance);
    }
}
