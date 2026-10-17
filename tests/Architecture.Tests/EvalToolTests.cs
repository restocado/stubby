using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Stubby.Architecture.Tests.StubbyArchitecture;

namespace Stubby.Architecture.Tests;

public class EvalToolTests
{
    [Fact]
    public void Eval_uses_no_web_app_or_database()
    {
        Types().That().ResideInAssembly(EvalAssembly)
            .Should().NotDependOnAnyTypesThat().ResideInAssembly(WebAssembly)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^{InfrastructureAssembly}\.Persistence(\..*)?$")
            .WithoutRequiringPositiveResults()
            .Check(Instance);
    }
}
