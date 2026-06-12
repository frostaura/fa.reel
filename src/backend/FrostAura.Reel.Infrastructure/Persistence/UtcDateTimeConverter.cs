using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FrostAura.Reel.Infrastructure.Persistence;

/// <summary>
/// Npgsql only writes Kind=Utc DateTimes to timestamptz. External payloads carry date-only
/// values ("1996-02-16" → Kind=Unspecified), so every DateTime is normalized model-wide:
/// Unspecified is declared UTC (Trakt/TMDB timestamps are UTC by contract), Local converts.
/// </summary>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v
                : v.Kind == DateTimeKind.Local ? v.ToUniversalTime()
                : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
