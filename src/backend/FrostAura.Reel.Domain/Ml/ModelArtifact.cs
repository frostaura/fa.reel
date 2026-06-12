using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Ml;

public enum ArtifactStatus
{
    Active,
    Superseded,
    Failed,
}

/// <summary>
/// A trained per-account model. Bytes live in Postgres (multi-instance safe, inside pg
/// backups; FastTree zips are &lt;5 MB). Exactly one Active artifact per account.
/// </summary>
public class ModelArtifact : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>Per-account incrementing version.</summary>
    public int Version { get; set; }

    public string Algo { get; set; } = "FastTree";

    public byte[] ArtifactBytes { get; set; } = [];

    /// <summary>Ordered feature schema (jsonb) the artifact was trained against.</summary>
    public string FeatureSchemaJson { get; set; } = "[]";

    public Guid TrainingRunId { get; set; }

    public ArtifactStatus Status { get; set; } = ArtifactStatus.Active;

    public DateTime TrainedAt { get; set; }
}
