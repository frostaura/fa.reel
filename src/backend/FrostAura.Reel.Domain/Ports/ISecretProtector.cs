namespace FrostAura.Reel.Domain.Ports;

/// <summary>
/// Encrypts/decrypts secrets at rest (Trakt OAuth tokens). Backed by ASP.NET Data Protection
/// with an EF-persisted key ring, so ciphertexts survive container restarts and re-deploys.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
