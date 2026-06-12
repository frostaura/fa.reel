namespace FrostAura.Reel.Application.Tenancy;

/// <summary>Scoped holder — one per HTTP request / per job execution scope.</summary>
public class AccountContext : IAccountContext
{
    public Guid? AccountId { get; private set; }

    public void SetAccount(Guid accountId) => AccountId = accountId;

    public void Clear() => AccountId = null;
}
