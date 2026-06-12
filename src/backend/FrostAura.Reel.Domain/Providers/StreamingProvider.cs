namespace FrostAura.Reel.Domain.Providers;

/// <summary>Streaming service (Netflix, Prime, …) from TMDB's provider registry.</summary>
public class StreamingProvider
{
    public Guid Id { get; set; }

    public int TmdbProviderId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? LogoPath { get; set; }

    public int DisplayPriority { get; set; }
}
