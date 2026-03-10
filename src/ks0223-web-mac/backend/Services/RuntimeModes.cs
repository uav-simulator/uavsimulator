namespace Ks0223.Web.Backend.Services;

public static class RuntimeModes
{
    public const string RealRobot = "real-robot";
    public const string UnitySim = "unity-sim";

    public static string Normalize(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return RealRobot;
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "real" or "real-robot" or "pi" or "robot" => RealRobot,
            "unity" or "unity-sim" or "sim" or "simulator" => UnitySim,
            _ => mode.Trim().ToLowerInvariant(),
        };
    }
}
