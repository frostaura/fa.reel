namespace FrostAura.Reel.Domain.Tenancy;

/// <summary>
/// Marker for entities owned by a single account (tenant). Every implementing entity gets a
/// global EF query filter on <see cref="AccountId"/> — row-level isolation is enforced once,
/// at the DbContext, not per-query. Catalog entities (titles, people, providers) are global
/// and deliberately do NOT implement this.
/// </summary>
public interface IAccountScoped
{
    Guid AccountId { get; set; }
}
