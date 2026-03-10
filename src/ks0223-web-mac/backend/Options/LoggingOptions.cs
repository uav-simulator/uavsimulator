namespace Ks0223.Web.Backend.Options;

public sealed class LoggingOptions
{
    public string Directory { get; set; } = "../logs";
    public int RecentFilesLimit { get; set; } = 20;
}
