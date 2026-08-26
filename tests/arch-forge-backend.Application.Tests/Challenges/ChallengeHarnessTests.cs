using arch_forge_backend.Application.Analysis.Rules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

public class ChallengeHarnessTests
{
    [Fact]
    public void The_harness_grades_a_challenge_with_the_same_rules_production_analyses_with()
    {
        var production = new ServiceCollection()
            .AddApplication()
            .Where(service => service.ServiceType == typeof(IArchitectureRule))
            .Select(service => service.ImplementationType!.Name)
            .OrderBy(name => name)
            .ToList();

        var harness = ChallengeHarness.RuleNames().OrderBy(name => name).ToList();

        Assert.Equal(production, harness);
    }
}
