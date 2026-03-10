using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartPanel.Api.Data;

namespace SmartPanel.Api.Services;

public sealed class MediaMtxService : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _protector;
    private readonly MediaMtxOptions _options;
    private readonly ILogger<MediaMtxService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private Process? _process;
    private string? _resolvedBinaryPath;
    private bool _resolvedBinaryPathComputed;
    private string _lastAppliedConfig = string.Empty;

    public MediaMtxService(
        IServiceScopeFactory scopeFactory,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<MediaMtxOptions> options,
        ILogger<MediaMtxService> logger)
    {
        _scopeFactory = scopeFactory;
        _protector = dataProtectionProvider.CreateProtector("SmartPanel.DirectCameras.Credentials.v1");
        _options = options.Value ?? new MediaMtxOptions();
        _logger = logger;
    }

    public bool HasLowLatencyPlayer(DirectCameraEntity entity) =>
        TryGetBinaryPath() is not null && GetAuthenticatedRtspSourceUrl(entity) is not null;

    public string GetWebRtcPlayerUrl(DirectCameraEntity entity) =>
        BuildPlayerUrl(_options.WebRtcPlayerBaseUrl, entity);

    public string GetHlsPlayerUrl(DirectCameraEntity entity) =>
        BuildPlayerUrl(_options.HlsPlayerBaseUrl, entity);

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);

        try
        {
            var binaryPath = TryGetBinaryPath();
            if (!_options.Enabled || binaryPath is null)
            {
                await StopProcessAsync();
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SmartPanelDbContext>();
            var cameras = await db.DirectCameras
                .AsNoTracking()
                .OrderBy(x => x.CameraId)
                .ToListAsync(cancellationToken);
            var config = BuildConfig(cameras);
            if (string.IsNullOrWhiteSpace(config))
            {
                await StopProcessAsync();
                return;
            }

            var workingDirectory = ResolveDirectoryPath(_options.WorkingDirectory, "mediamtx");
            Directory.CreateDirectory(workingDirectory);
            var configPath = ResolveFilePath(_options.ConfigFilePath, workingDirectory, "mediamtx.yml");
            var configChanged = !string.Equals(config, _lastAppliedConfig, StringComparison.Ordinal);

            if (configChanged || !File.Exists(configPath))
            {
                await File.WriteAllTextAsync(configPath, config, new UTF8Encoding(false), cancellationToken);
                _lastAppliedConfig = config;
            }

            await EnsureProcessRunningAsync(binaryPath, configPath, workingDirectory, configChanged, cancellationToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            StopProcessAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore shutdown races.
        }

        _syncLock.Dispose();
    }

    private string BuildPlayerUrl(string? baseUrl, DirectCameraEntity entity)
    {
        if (!HasLowLatencyPlayer(entity))
        {
            return string.Empty;
        }

        var normalizedBaseUrl = NormalizeOptional(baseUrl);
        if (normalizedBaseUrl is null || !Uri.TryCreate(AppendTrailingSlash(normalizedBaseUrl), UriKind.Absolute, out var root))
        {
            return string.Empty;
        }

        return new Uri(root, GetPathName(entity)).ToString();
    }

    private string BuildConfig(IReadOnlyCollection<DirectCameraEntity> cameras)
    {
        var entries = cameras
            .Select(entity => new
            {
                Entity = entity,
                SourceUrl = GetAuthenticatedRtspSourceUrl(entity)
            })
            .Where(x => x.SourceUrl is not null)
            .ToList();
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendYamlRaw(builder, "logLevel", NormalizeOptional(_options.LogLevel) ?? "warn");
        AppendYamlRaw(builder, "api", "true");
        AppendYamlKeyValue(builder, "apiAddress", NormalizeOptional(_options.ApiAddress) ?? "127.0.0.1:9997");
        AppendYamlRaw(builder, "rtsp", "true");
        AppendYamlKeyValue(builder, "rtspAddress", NormalizeOptional(_options.RtspAddress) ?? "127.0.0.1:8554");
        AppendYamlRaw(builder, "hls", "true");
        AppendYamlKeyValue(builder, "hlsAddress", NormalizeOptional(_options.HlsAddress) ?? "127.0.0.1:8888");
        AppendYamlArray(builder, "hlsAllowOrigins", ["*"]);
        AppendYamlRaw(builder, "hlsVariant", NormalizeOptional(_options.HlsVariant) ?? "lowLatency");
        AppendYamlRaw(builder, "hlsSegmentDuration", NormalizeOptional(_options.HlsSegmentDuration) ?? "1s");
        AppendYamlRaw(builder, "hlsPartDuration", NormalizeOptional(_options.HlsPartDuration) ?? "200ms");
        AppendYamlRaw(builder, "hlsAlwaysRemux", _options.HlsAlwaysRemux ? "true" : "false");
        AppendYamlRaw(builder, "webrtc", "true");
        AppendYamlKeyValue(builder, "webrtcAddress", NormalizeOptional(_options.WebRtcAddress) ?? "127.0.0.1:8889");
        AppendYamlArray(builder, "webrtcAllowOrigins", ["*"]);

        var webrtcLocalUdpAddress = NormalizeOptional(_options.WebRtcLocalUdpAddress);
        if (webrtcLocalUdpAddress is not null)
        {
            AppendYamlKeyValue(builder, "webrtcLocalUDPAddress", webrtcLocalUdpAddress);
        }

        var webrtcLocalTcpAddress = NormalizeOptional(_options.WebRtcLocalTcpAddress);
        if (webrtcLocalTcpAddress is not null)
        {
            AppendYamlKeyValue(builder, "webrtcLocalTCPAddress", webrtcLocalTcpAddress);
        }

        var additionalHosts = _options.WebRtcAdditionalHosts
            .Select(NormalizeOptional)
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (additionalHosts.Count > 0)
        {
            AppendYamlArray(builder, "webrtcAdditionalHosts", additionalHosts);
        }

        builder.AppendLine("pathDefaults:");
        AppendYamlRaw(builder, "sourceOnDemand", _options.SourceOnDemand ? "true" : "false", 1);
        if (_options.SourceOnDemand)
        {
            AppendYamlRaw(builder, "sourceOnDemandStartTimeout", "10s", 1);
            AppendYamlRaw(builder, "sourceOnDemandCloseAfter", "10s", 1);
        }
        AppendYamlRaw(builder, "rtspTransport", NormalizeOptional(_options.SourceRtspTransport) ?? "automatic", 1);

        builder.AppendLine("paths:");
        foreach (var entry in entries)
        {
            builder.AppendLine($"  {GetPathName(entry.Entity)}:");
            AppendYamlKeyValue(builder, "source", entry.SourceUrl!, 2);
        }

        return builder.ToString();
    }

    private async Task EnsureProcessRunningAsync(
        string binaryPath,
        string configPath,
        string workingDirectory,
        bool restartRequested,
        CancellationToken cancellationToken)
    {
        if (restartRequested)
        {
            await StopProcessAsync();
        }

        if (_process is not null && !_process.HasExited)
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(configPath);

        try
        {
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("MediaMTX could not be started.");
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                _logger.LogWarning("MediaMTX exited with code {ExitCode}.", process.ExitCode);
                process.Dispose();
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            };

            _process = process;
            _ = Task.Run(() => PumpOutputAsync(process.StandardOutput, "stdout"));
            _ = Task.Run(() => PumpOutputAsync(process.StandardError, "stderr"));

            await Task.Delay(750, cancellationToken);
            if (process.HasExited)
            {
                throw new InvalidOperationException($"MediaMTX exited immediately with code {process.ExitCode}.");
            }

            _logger.LogInformation("MediaMTX started from {BinaryPath} using {ConfigPath}.", binaryPath, configPath);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Unable to start MediaMTX from '{binaryPath}'. {ex.Message}",
                ex);
        }
    }

    private async Task StopProcessAsync()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore process teardown races.
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch
        {
            // Ignore process teardown races.
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task PumpOutputAsync(StreamReader reader, string streamName)
    {
        try
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogDebug("MediaMTX {StreamName}: {Line}", streamName, line);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MediaMTX {StreamName} logger terminated.", streamName);
        }
    }

    private string? GetAuthenticatedRtspSourceUrl(DirectCameraEntity entity)
    {
        var sourceUrl = NormalizeOptional(entity.LiveStreamUri) ?? NormalizeOptional(entity.StreamUri);
        if (sourceUrl is null)
        {
            return null;
        }

        var username = NormalizeOptional(entity.Username);
        if (username is null)
        {
            return sourceUrl;
        }

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return sourceUrl;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = username,
            Password = Unprotect(entity.PasswordProtected) ?? string.Empty
        };

        return builder.Uri.ToString();
    }

    private string GetPathName(DirectCameraEntity entity) =>
        $"direct_{entity.CameraId.ToLowerInvariant()}";

    private string? TryGetBinaryPath()
    {
        if (_resolvedBinaryPathComputed)
        {
            return _resolvedBinaryPath;
        }

        _resolvedBinaryPathComputed = true;
        var configured = NormalizeOptional(_options.BinaryPath) ?? "mediamtx";
        _resolvedBinaryPath = ResolveExecutablePath(configured);
        if (_resolvedBinaryPath is null)
        {
            _logger.LogDebug("MediaMTX executable '{ConfiguredPath}' was not found.", configured);
        }

        return _resolvedBinaryPath;
    }

    private static string ResolveDirectoryPath(string? configuredPath, string fallbackDirectoryName)
    {
        var normalized = NormalizeOptional(configuredPath);
        if (normalized is not null)
        {
            return Path.GetFullPath(normalized);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, fallbackDirectoryName));
    }

    private static string ResolveFilePath(string? configuredPath, string workingDirectory, string fallbackFileName)
    {
        var normalized = NormalizeOptional(configuredPath);
        if (normalized is not null)
        {
            return Path.GetFullPath(normalized);
        }

        return Path.Combine(workingDirectory, fallbackFileName);
    }

    private static void AppendYamlKeyValue(StringBuilder builder, string key, string value, int indentLevel = 0)
    {
        builder.Append(' ', indentLevel * 2);
        builder.Append(key);
        builder.Append(": ");
        builder.AppendLine(YamlQuote(value));
    }

    private static void AppendYamlRaw(StringBuilder builder, string key, string value, int indentLevel = 0)
    {
        builder.Append(' ', indentLevel * 2);
        builder.Append(key);
        builder.Append(": ");
        builder.AppendLine(value);
    }

    private static void AppendYamlArray(StringBuilder builder, string key, IEnumerable<string> values, int indentLevel = 0)
    {
        builder.Append(' ', indentLevel * 2);
        builder.Append(key);
        builder.AppendLine(":");

        foreach (var value in values)
        {
            builder.Append(' ', (indentLevel + 1) * 2);
            builder.Append("- ");
            builder.AppendLine(YamlQuote(value));
        }
    }

    private static string AppendTrailingSlash(string value) =>
        value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";

    private static string YamlQuote(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(value);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveExecutablePath(string configuredPath)
    {
        if (configuredPath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
            Path.IsPathRooted(configuredPath))
        {
            if (File.Exists(configuredPath))
            {
                return configuredPath;
            }

            if (OperatingSystem.IsWindows() &&
                !Path.HasExtension(configuredPath) &&
                File.Exists($"{configuredPath}.exe"))
            {
                return $"{configuredPath}.exe";
            }

            return null;
        }

        var candidates = Path.HasExtension(configuredPath)
            ? new[] { configuredPath }
            : OperatingSystem.IsWindows()
                ? new[] { $"{configuredPath}.exe", configuredPath }
                : new[] { configuredPath };
        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pathEntry in pathEntries)
        {
            var directory = pathEntry.Trim().Trim('"');
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(directory, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
