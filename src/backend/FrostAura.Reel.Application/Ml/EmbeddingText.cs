using System.Security.Cryptography;
using System.Text;
using FrostAura.Reel.Domain.Catalog;

namespace FrostAura.Reel.Application.Ml;

/// <summary>
/// THE single source of the plot/tone text an embedding is computed from — name, year, genres,
/// overview. Shared by the nightly enrich job and the live search-expansion path so a title
/// embedded on demand carries the IDENTICAL <see cref="Hash"/> the enrich job would produce; the
/// two paths never fight over the same row or freeze a stale vector in place.
/// </summary>
public static class EmbeddingText
{
    /// <summary>The embedding model id stamped on every row — keep both write paths uniform.</summary>
    public const string Model = "text-embedding-3-small";

    public static string Build(Title title)
    {
        var sb = new StringBuilder();
        sb.Append(title.Name);
        if (title.Year is { } year)
        {
            sb.Append(" (").Append(year).Append(')');
        }

        if (title.Genres.Length > 0)
        {
            sb.Append(". ").Append(string.Join(", ", title.Genres));
        }

        if (!string.IsNullOrWhiteSpace(title.Overview))
        {
            sb.Append(". ").Append(title.Overview);
        }

        return sb.ToString();
    }

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
