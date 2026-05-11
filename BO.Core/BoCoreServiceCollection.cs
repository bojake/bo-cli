using BO.Core.Configuration;
using BO.Core.Ids;
using BO.Core.Indexing;
using BO.Core.Persistence;
using BO.Core.Persistence.BogDb;
using BO.Core.Persistence.InMemory;
using BO.Core.Services.Bootstrap;
using BO.Core.Services.Index;
using Microsoft.Extensions.DependencyInjection;

namespace BO.Core;

public static class BoCoreServiceCollection
{
    /// <summary>
    /// Registers BO services with a BogDB persistent graph store rooted at
    /// &lt;workspaceRoot&gt;/.bo/graph.
    /// </summary>
    public static IServiceCollection AddBoCore(this IServiceCollection services, string workspaceRoot)
    {
        var dbPath = Path.Combine(workspaceRoot, ".bo", "graph");
        services.AddSingleton(new BogDbStorageOptions(dbPath));
        services.AddSingleton<IBoGraphStore, BogDbGraphStore>();
        return AddBoCoreInternals(services);
    }

    /// <summary>
    /// Registers BO services with an in-memory graph store.
    /// Use this in tests or when persistence is not needed.
    /// </summary>
    public static IServiceCollection AddBoCore(this IServiceCollection services)
    {
        services.AddSingleton<IBoGraphStore, InMemoryGraphStore>();
        return AddBoCoreInternals(services);
    }

    private static IServiceCollection AddBoCoreInternals(IServiceCollection services)
    {
        services.AddSingleton<ArtifactLoader>();
        services.AddSingleton<BoIdGenerator>();
        services.AddSingleton<WorkspaceScanner>();
        services.AddSingleton<SourceSymbolExtractor>();
        services.AddSingleton<ContractExtractor>();
        services.AddSingleton<DependencyExtractor>();
        services.AddSingleton<SymbolDependencyExtractor>();
        services.AddSingleton<BoundaryExtractor>();
        services.AddSingleton<EffectProfileDeriver>();
        services.AddSingleton<ComplexityProfileDeriver>();
        services.AddSingleton<ResponsibilityProfileDeriver>();
        services.AddSingleton<ContextBurdenDeriver>();
        services.AddSingleton<RefactorPressureScorer>();
        services.AddSingleton<RefactorDecisionDeriver>();
        services.AddSingleton<SeamExtractionPlanner>();
        services.AddSingleton<BootstrapService>();
        services.AddSingleton<IndexWorkspaceService>();
        return services;
    }
}
