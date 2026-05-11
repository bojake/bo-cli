using BO.Core.Indexing;

namespace BO.Tests;

public sealed class EffectProfileDeriverTests
{
    [Fact]
    public void Derive_BuildsConservativeProfileFromBoundaryInteractions()
    {
        var file = new FileRecord(
            "file:repo:test:src/service.ts",
            "repo:test",
            "/tmp/src/service.ts",
            "src/service.ts",
            "typescript",
            false,
            false,
            "module:repo:test:src");

        var interactions = new[]
        {
            new BoundaryInteractionRecord("boundary:1", file.Id, "http", "read", "axios.get", "external", 0.9),
            new BoundaryInteractionRecord("boundary:2", file.Id, "logging", "log", "logger.error", "observable", 0.9)
        };

        var deriver = new EffectProfileDeriver();
        var profiles = deriver.Derive([file], interactions);

        Assert.Single(profiles);
        Assert.True(profiles[0].ReadsState);
        Assert.False(profiles[0].WritesState);
        Assert.True(profiles[0].CallsExternalService);
        Assert.True(profiles[0].HasLoggingLogic);
        Assert.Contains("http", profiles[0].SideEffectClasses);
        Assert.Contains("log", profiles[0].SideEffectClasses);
    }

    [Fact]
    public void Derive_UsesConfiguredEffectRules()
    {
        var file = new FileRecord(
            "file:repo:test:src/service.ts",
            "repo:test",
            "/tmp/src/service.ts",
            "src/service.ts",
            "typescript",
            false,
            false,
            "module:repo:test:src");

        var interactions = new[]
        {
            new BoundaryInteractionRecord("boundary:1", file.Id, "ledger", "lookup", "ledger.find", "external", 0.9),
            new BoundaryInteractionRecord("boundary:2", file.Id, "audit_log", "append_event", "audit.append", "observable", 0.9)
        };
        var rules = SemanticProfileRules.Default with
        {
            EffectRules = new EffectDerivationRules(
                ["lookup"],
                ["append_event"],
                ["append_event"],
                ["ledger"],
                [],
                [],
                ["audit_log"])
        };

        var deriver = new EffectProfileDeriver();
        var profiles = deriver.Derive([file], interactions, rules);

        Assert.Single(profiles);
        Assert.True(profiles[0].ReadsState);
        Assert.True(profiles[0].WritesState);
        Assert.True(profiles[0].EmitsEvents);
        Assert.True(profiles[0].CallsExternalService);
        Assert.True(profiles[0].HasLoggingLogic);
    }
}
