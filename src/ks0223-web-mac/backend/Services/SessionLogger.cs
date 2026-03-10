using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Options;
using Microsoft.Extensions.Options;

namespace Ks0223.Web.Backend.Services;

public sealed class SessionLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly string logsDirectory;
    private readonly int recentFilesLimit;
    private StreamWriter? writer;
    private string? currentFile;

    public SessionLogger(IOptions<LoggingOptions> options, IWebHostEnvironment env)
    {
        var configured = options.Value.Directory;
        logsDirectory = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
        recentFilesLimit = Math.Clamp(options.Value.RecentFilesLimit, 1, 200);
        Directory.CreateDirectory(logsDirectory);
    }

    public string LogsDirectory => logsDirectory;

    public LogState GetState() => new(IsLogging: writer is not null, CurrentFile: currentFile);

    public async Task<LogState> StartAsync(string? tag, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (writer is not null)
            {
                return GetState();
            }

            var safeTag = SanitizeTag(tag);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = string.IsNullOrWhiteSpace(safeTag)
                ? $"session_{stamp}.jsonl"
                : $"session_{stamp}_{safeTag}.jsonl";

            currentFile = Path.Combine(logsDirectory, fileName);
            writer = new StreamWriter(new FileStream(currentFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Encoding.UTF8)
            {
                AutoFlush = true,
            };

            await WriteInternalAsync("log.started", new { tag = safeTag }, cancellationToken);
            return GetState();
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<LogState> StopAsync(CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (writer is null)
            {
                return GetState();
            }

            await WriteInternalAsync("log.stopped", new { }, cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await writer.DisposeAsync();
            writer = null;
            currentFile = null;
            return GetState();
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task WriteAsync(string type, object payload, CancellationToken cancellationToken = default)
    {
        if (writer is null)
        {
            return;
        }

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (writer is null)
            {
                return;
            }

            await WriteInternalAsync(type, payload, cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public IReadOnlyList<LogFileInfo> ListRecentFiles()
    {
        var dir = new DirectoryInfo(logsDirectory);
        if (!dir.Exists)
        {
            return Array.Empty<LogFileInfo>();
        }

        return dir.GetFiles("session_*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(recentFilesLimit)
            .Select(file => new LogFileInfo(
                Name: file.Name,
                AbsolutePath: file.FullName,
                SizeBytes: file.Length,
                LastWriteTimeUtc: DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc)))
            .ToList();
    }

    public Task OpenFolderAsync()
    {
        var opener = GetOpenerCommand();
        if (opener is null)
        {
            return Task.CompletedTask;
        }

        var psi = new ProcessStartInfo
        {
            FileName = opener,
            ArgumentList = { logsDirectory },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            Process.Start(psi);
        }
        catch
        {
            // In containers or headless environments there may be no GUI opener.
        }

        return Task.CompletedTask;
    }

    private async Task WriteInternalAsync(string type, object payload, CancellationToken cancellationToken)
    {
        var envelope = new
        {
            timestamp = DateTimeOffset.UtcNow,
            type,
            payload,
        };

        var line = JsonSerializer.Serialize(envelope, JsonOptions);
        await writer!.WriteLineAsync(line.AsMemory(), cancellationToken);
    }

    private static string? SanitizeTag(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }

        var value = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(40, value.Length)];
    }

    private static string? GetOpenerCommand()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "open";
        }

        if (OperatingSystem.IsWindows())
        {
            return "explorer";
        }

        if (OperatingSystem.IsLinux())
        {
            return "xdg-open";
        }

        return null;
    }
}
