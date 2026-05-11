using BO.Core.Indexing;

namespace BO.Tests;

public sealed class RefactorPressureScorerTests
{
    [Fact]
    public void Score_FiresGodClassGate_ForLargeHighlyComplexFile()
    {
        var scorer = new RefactorPressureScorer();

        var complexity = new ComplexityProfileRecord(
            "complexity:file:run-ops",
            "file:run-ops",
            "file",
            514,
            141,
            37,
            6,
            68,
            36,
            2,
            0,
            0,
            0.9);

        var responsibility = new ResponsibilityProfileRecord(
            "responsibility:file:run-ops",
            "file:run-ops",
            "file",
            1,
            1,
            2,
            2,
            0,
            ["querying", "run_management"],
            0.8);

        var scores = scorer.Score([complexity], [responsibility], []);

        var score = Assert.Single(scores);
        Assert.Contains("god_class", score.FiredGates);
        Assert.NotEqual("none", score.Recommendation);
    }

    [Fact]
    public void Score_UsesConfiguredGodClassGateThresholds()
    {
        var scorer = new RefactorPressureScorer();
        var rules = RefactorScoringRules.Default with
        {
            HardPivotGates = RefactorScoringRules.Default.HardPivotGates with
            {
                GodClass = new GodClassGate(true, Loc: 1000, CognitiveComplexity: 250)
            }
        };

        var complexity = new ComplexityProfileRecord(
            "complexity:file:run-ops",
            "file:run-ops",
            "file",
            514,
            141,
            37,
            6,
            68,
            36,
            2,
            0,
            0,
            0.9);

        var responsibility = new ResponsibilityProfileRecord(
            "responsibility:file:run-ops",
            "file:run-ops",
            "file",
            1,
            1,
            2,
            2,
            0,
            ["querying", "run_management"],
            0.8);

        var scores = scorer.Score([complexity], [responsibility], [], rules);

        var score = Assert.Single(scores);
        Assert.DoesNotContain("god_class", score.FiredGates);
    }
}
