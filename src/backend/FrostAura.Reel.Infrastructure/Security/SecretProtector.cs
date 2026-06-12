using FrostAura.Reel.Domain.Ports;
using Microsoft.AspNetCore.DataProtection;

namespace FrostAura.Reel.Infrastructure.Security;

/// <summary>Data-Protection-backed secret encryption (EF-persisted key ring).</summary>
public class SecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("FrostAura.Reel.Secrets.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
