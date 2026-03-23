using ContentAgent.Api.Services;

namespace ContentAgent.Api.Tests;

public sealed class BufferSchedulingTests
{
    [Fact]
    public void NextUtcWallTime_UsesUtcAndRequestedClock()
    {
        var n = BufferScheduling.NextUtcWallTime(19, 30);
        Assert.Equal(TimeSpan.Zero, n.Offset);
        Assert.Equal(19, n.Hour);
        Assert.Equal(30, n.Minute);
    }

    [Fact]
    public void NextUtcWallTime_IsStrictlyInTheFuture()
    {
        var before = DateTimeOffset.UtcNow;
        var n = BufferScheduling.NextUtcWallTime(19, 0);
        Assert.True(n > before, $"Expected {n} > {before}");
    }
}
