namespace BO.Core.Persistence.BogDb;

/// <summary>
/// Configuration for the BogDB-backed persistent graph store.
/// </summary>
public sealed record BogDbStorageOptions(string DatabasePath);

