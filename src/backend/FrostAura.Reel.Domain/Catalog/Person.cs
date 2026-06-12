namespace FrostAura.Reel.Domain.Catalog;

/// <summary>Cast/crew person — global catalog, hydrated from TMDB credits.</summary>
public class Person
{
    public Guid Id { get; set; }

    public long TmdbId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? KnownForDepartment { get; set; }

    public string? ProfilePath { get; set; }
}
