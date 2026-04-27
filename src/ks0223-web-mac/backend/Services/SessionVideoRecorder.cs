using System.Diagnostics;

namespace Ks0223.Web.Backend.Services;

public sealed class SessionVideoRecorder : IDisposable
{
    private readonly string outputDirectory;
    private readonly ILogger<SessionVideoRecorder> logger;
    private Process? process;
    private string? currentFile;

    public SessionVideoRecorder(IWebHostEnvironment env, ILogger<SessionVideoRecorder> logger)
    {
        outputDirectory = Path.GetFullPath(Path.Combine(env.ContentRootPath, "runtime-data", "autopilot-recordings"));
        Directory.CreateDirectory(outputDirectory);
        this.logger = logger;
    }

    public string? CurrentFile => currentFile;

    public bool TryStart(string mjpegUrl, string tag)
    {
        Stop();

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeTag = string.Concat(tag.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        if (string.IsNullOrEmpty(safeTag))
        {
            safeTag = "session";
        }
        var path = Path.Combine(outputDirectory, $"autopilot_{stamp}_{safeTag}.mp4");

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("warning");
        psi.ArgumentList.Add("-fflags");
        psi.ArgumentList.Add("nobuffer");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(mjpegUrl);
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-preset");
        psi.ArgumentList.Add("ultrafast");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");
        // 2026-04-27: switched from `+faststart` (writes moov atom only after
        // input ends, so a SIGTERM in mid-recording leaves a 48-byte stub)
        // to `+frag_keyframe+empty_moov+default_base_moof` which produces
        // a fragmented MP4. Each ~1 s segment is self-contained and the
        // file is valid even if ffmpeg is killed before the input EOF.
        // Required for short demo-recordings (1–10 s) where the autopilot
        // or the user's `Stop demo` button cuts the stream early.
        psi.ArgumentList.Add("-movflags");
        psi.ArgumentList.Add("+frag_keyframe+empty_moov+default_base_moof");
        psi.ArgumentList.Add("-frag_duration");
        psi.ArgumentList.Add("1000000");
        psi.ArgumentList.Add(path);

        try
        {
            process = Process.Start(psi);
            if (process is null)
            {
                logger.LogWarning("ffmpeg did not start for {Path}", path);
                return false;
            }
            currentFile = path;
            logger.LogInformation("Autopilot recording started: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to start ffmpeg recorder (is ffmpeg in PATH?)");
            process = null;
            currentFile = null;
            return false;
        }
    }

    public string? Stop()
    {
        var path = currentFile;
        var p = process;
        process = null;
        currentFile = null;

        if (p is null)
        {
            return null;
        }

        try
        {
            if (!p.HasExited)
            {
                try
                {
                    // ffmpeg finalizes mp4 cleanly on 'q' (or SIGTERM via Kill below).
                    p.StandardInput.WriteLine("q");
                    p.StandardInput.Flush();
                }
                catch
                {
                    // fall through to Kill
                }

                if (!p.WaitForExit(2500))
                {
                    p.Kill();
                    p.WaitForExit(1500);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error stopping ffmpeg recorder");
        }
        finally
        {
            p.Dispose();
        }

        logger.LogInformation("Autopilot recording stopped: {Path}", path);
        return path;
    }

    public void Dispose() => Stop();
}
