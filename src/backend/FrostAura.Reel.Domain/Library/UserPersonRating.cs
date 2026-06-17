using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Library;

/// <summary>
/// An explicit 1–10 rating the user gave a Person (actor/director/writer) directly — distinct
/// from the affinity Reel derives from title ratings. Fed into the model through the existing
/// person-affinity signal (a strong pseudo-observation that overrides the derived mean for that
/// person). <see cref="RatedAt"/> is the leakage-clean time axis — training filters `≤ asOf`,
/// exactly like <see cref="UserRating"/>. Not synced to Trakt (Trakt has no person rating).
/// </summary>
public class UserPersonRating : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid PersonId { get; set; }

    public short Rating { get; set; }

    public RatingSource Source { get; set; } = RatingSource.Reel;

    public DateTime RatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
