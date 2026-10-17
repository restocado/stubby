using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Stubby.Architecture.Tests.StubbyArchitecture;

namespace Stubby.Architecture.Tests;

public class AiGatewayIsolationTests
{
    private const string GatewayNamespace = $@"^{InfrastructureAssembly}\.AiGateway(\..*)?$";

    [Fact]
    public void AiGateway_knows_nothing_about_receipts()
    {
        Types().That().ResideInNamespaceMatching(GatewayNamespace)
            .Should().NotDependOnAnyTypesThat().ResideInAssembly(DomainAssembly)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^{ApplicationAssembly}\.Features(\..*)?$")
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^{InfrastructureAssembly}\.(Persistence|Storage|Pdf|Extraction)(\..*)?$")
            .WithoutRequiringPositiveResults()
            .Check(Instance);
    }
}
