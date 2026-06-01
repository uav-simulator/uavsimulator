namespace Ks0223.Web.Backend.Services;

internal static class CameraFrameFreshness
{
    public static bool IsFresh(DateTimeOffset? timestamp, int maxFrameAgeMs, DateTimeOffset now)
    {
        if (timestamp is null)
        {
            return false;
        }

        if (maxFrameAgeMs <= 0)
        {
            return true;
        }

        var maxAge = TimeSpan.FromMilliseconds(maxFrameAgeMs);
        return now - timestamp.Value <= maxAge;
    }
}
