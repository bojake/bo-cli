using BO.Core.Indexing;

namespace BO.Tests;

public sealed class ResponsibilityProfileDeriverTests
{
    [Fact]
    public void Derive_InfersWorkflowRolesFromBoundaryAndEffectSignals()
    {
        var file = new FileRecord(
            "file:repo:test:service.ts",
            "repo:test",
            "/tmp/service.ts",
            "service.ts",
            "typescript",
            false,
            false,
            "module:repo:test:root");

        var dependencies = new[]
        {
            new FileDependencyRecord("edge:1", file.Id, "file:repo:test:dep.ts", "./dep", false, true)
        };

        var boundaries = new[]
        {
            new BoundaryInteractionRecord("boundary:1", file.Id, "db", "read", "client.query", "external", 0.9),
            new BoundaryInteractionRecord("boundary:2", file.Id, "http", "read", "axios.get", "external", 0.9)
        };

        var effects = new[]
        {
            new EffectProfileRecord(
                "effect:file:repo:test:service.ts",
                file.Id,
                "file",
                true,
                false,
                false,
                true,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                ["db", "http", "read"],
                0.9)
        };

        var deriver = new ResponsibilityProfileDeriver();
        var profiles = deriver.Derive([file], dependencies, boundaries, effects);

        Assert.Single(profiles);
        Assert.Equal(2, profiles[0].BoundaryTypeCount);
        Assert.Equal(1, profiles[0].DependencyCategoryCount);
        Assert.Equal(3, profiles[0].SideEffectClassCount);
        Assert.Contains("persistence", profiles[0].DominantResponsibilities);
        Assert.Contains("transport", profiles[0].DominantResponsibilities);
        Assert.Contains("orchestration", profiles[0].DominantResponsibilities);
    }

    [Fact]
    public void Derive_UsesConfiguredWorkflowRoles()
    {
        var file = new FileRecord(
            "file:repo:test:service.ts",
            "repo:test",
            "/tmp/service.ts",
            "service.ts",
            "typescript",
            false,
            false,
            "module:repo:test:root");

        var boundaries = new[]
        {
            new BoundaryInteractionRecord("boundary:1", file.Id, "ledger", "lookup", "ledger.find", "external", 0.9)
        };
        var effects = new[]
        {
            new EffectProfileRecord(
                "effect:file:repo:test:service.ts",
                file.Id,
                "file",
                true,
                false,
                true,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                ["ledger", "lookup"],
                0.9)
        };
        var rules = SemanticProfileRules.Default with
        {
            ResponsibilityRules = new ResponsibilityDerivationRules(
                [
                    new WorkflowRoleRule("financial_records", ["ledger"], []),
                    new WorkflowRoleRule("eventing", [], ["emits_events"])
                ],
                "coordination",
                2)
        };

        var deriver = new ResponsibilityProfileDeriver();
        var profiles = deriver.Derive([file], [], boundaries, effects, rules);

        Assert.Single(profiles);
        Assert.Contains("financial_records", profiles[0].DominantResponsibilities);
        Assert.Contains("eventing", profiles[0].DominantResponsibilities);
        Assert.DoesNotContain("coordination", profiles[0].DominantResponsibilities);
    }
}
