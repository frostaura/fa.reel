using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace FrostAura.Reel.Application.Auth;

/// <summary>
/// HMAC-signed OAuth state: the server vouches for every state it hands out, independent of
/// the SPA's sessionStorage echo. Payload carries a nonce + 15-minute expiry; tampering or
/// staleness fails verification.
/// </summary>
public class OAuthStateCodec
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly byte[] _key;

    public OAuthStateCodec(IConfiguration configuration)
    {
        var secret = configuration["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is required.");
        // Purpose-separated from the JWT signing key.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("reel.oauth-state.v1:" + secret));
    }

    private record StatePayload(string Nonce, long Exp);

    public string Issue()
    {
        var payload = new StatePayload(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds());
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = HMACSHA256.HashData(_key, payloadBytes);
        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    public bool Validate(string state)
    {
        var parts = state.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<StatePayload>(payloadBytes);
            return payload is not null && DateTimeOffset.FromUnixTimeSeconds(payload.Exp) > DateTimeOffset.UtcNow;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }
}
