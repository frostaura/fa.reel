using FrostAura.Reel.Application.Auth;
using Microsoft.Extensions.Configuration;

namespace FrostAura.Reel.Tests.Auth;

public class OAuthStateCodecTests
{
    private static OAuthStateCodec CreateCodec(string secret = "unit-test-secret-at-least-32-chars!!") =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JWT_SECRET"] = secret })
            .Build());

    [Fact]
    public void Issued_state_validates()
    {
        var codec = CreateCodec();
        var state = codec.Issue();
        Assert.True(codec.Validate(state));
    }

    [Fact]
    public void Tampered_payload_fails()
    {
        var codec = CreateCodec();
        var state = codec.Issue();
        var parts = state.Split('.');
        var tampered = parts[0][..^2] + "AA" + "." + parts[1];
        Assert.False(codec.Validate(tampered));
    }

    [Fact]
    public void State_from_a_different_secret_fails()
    {
        var state = CreateCodec("secret-one-padded-to-enough-length!!").Issue();
        Assert.False(CreateCodec("secret-two-padded-to-enough-length!!").Validate(state));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    [InlineData("!!notbase64!!.alsonot")]
    public void Malformed_states_fail_without_throwing(string state)
    {
        Assert.False(CreateCodec().Validate(state));
    }
}
