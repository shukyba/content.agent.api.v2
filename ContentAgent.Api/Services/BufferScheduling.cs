namespace ContentAgent.Api.Services;

public static class BufferScheduling
{
    /// <summary>Next occurrence of <paramref name="hourUtc"/>:<paramref name="minuteUtc"/> today or tomorrow (UTC).</summary>
    public static DateTimeOffset NextUtcWallTime(int hourUtc, int minuteUtc)
    {
        var now = DateTimeOffset.UtcNow;
        var candidate = new DateTimeOffset(now.Year, now.Month, now.Day, hourUtc, minuteUtc, 0, TimeSpan.Zero);
        if (now < candidate)
            return candidate;
        return candidate.AddDays(1);
    }
}
