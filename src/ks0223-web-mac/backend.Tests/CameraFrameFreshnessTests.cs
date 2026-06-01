using Ks0223.Web.Backend.Services;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class CameraFrameFreshnessTests
{
    [Fact]
    public void IsFresh_ReturnsFalse_WhenFrameIsOlderThanConfiguredLimit()
    {
        var now = new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero);
        var staleFrameAt = now - TimeSpan.FromSeconds(6);

        Assert.False(CameraFrameFreshness.IsFresh(staleFrameAt, maxFrameAgeMs: 5000, now));
    }

    [Fact]
    public void IsFresh_ReturnsTrue_WhenFrameIsWithinConfiguredLimit()
    {
        var now = new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero);
        var recentFrameAt = now - TimeSpan.FromSeconds(3);

        Assert.True(CameraFrameFreshness.IsFresh(recentFrameAt, maxFrameAgeMs: 5000, now));
    }

    [Fact]
    public void IsFresh_ReturnsFalse_WhenNoFrameTimestampExists()
    {
        var now = new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero);

        Assert.False(CameraFrameFreshness.IsFresh(null, maxFrameAgeMs: 5000, now));
    }
}
