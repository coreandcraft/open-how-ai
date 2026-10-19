using Journey.Server;

namespace Journey.Tests;

public class RequestTelemetryTests
{
    [Theory]
    [InlineData("/api/health", true)]
    [InlineData("/api/health?probe=1", true)]
    [InlineData("/API/HEALTH", true)]
    [InlineData("/api/healthcheck", false)]
    [InlineData("/api/journeys", false)]
    public void HealthChecksAreIdentifiedWithoutMatchingPrefixes(string value, bool health) =>
        Assert.Equal(health, RequestTelemetry.IsHealthCheck(value));

    [Theory]
    [InlineData("/api/journeys/abc", "/api/journeys/abc")]
    [InlineData("/api/invitations/abc123/accept", "/api/invitations/{redacted}/accept")]
    [InlineData("/api/invitations/abc123", "/api/invitations/{redacted}")]
    [InlineData(
        "https://openhow.me/api/auth/external-callback?code=secret&state=secret",
        "https://openhow.me/api/auth/external-callback")]
    [InlineData(
        "https://openhow.me/api/invitations/abc123/accept?x=1#code",
        "https://openhow.me/api/invitations/{redacted}/accept")]
    public void QueryStringsAndInvitationTokensAreRemoved(string value, string expected) =>
        Assert.Equal(expected, RequestTelemetry.Redact(value));
}
