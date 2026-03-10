using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartPanel.Api.Data;
using SmartPanel.Shared;

namespace SmartPanel.Api.Services;

public class DirectCameraService : IDirectCameraService
{
    private const string DirectPrefix = "direct:";
    private const string DeviceNamespaceV10 = "http://www.onvif.org/ver10/device/wsdl";
    private const string MediaNamespaceV1 = "http://www.onvif.org/ver10/media/wsdl";
    private const string MediaNamespaceV2 = "http://www.onvif.org/ver20/media/wsdl";
    private const string PtzNamespaceV20 = "http://www.onvif.org/ver20/ptz/wsdl";
    private const string DiscoveryAddress = "239.255.255.250";
    private const int DiscoveryPort = 3702;
    private static readonly TimeSpan DiscoveryWindow = TimeSpan.FromSeconds(4);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Wsse = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private static readonly XNamespace Wsu = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private static readonly XNamespace Wsa = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
    private static readonly XNamespace DiscoveryNs = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
    private static readonly XNamespace OnvifDiscovery = "http://www.onvif.org/ver10/network/wsdl";
    private static readonly XNamespace DeviceNs = DeviceNamespaceV10;
    private static readonly XNamespace MediaNs = MediaNamespaceV1;
    private static readonly XNamespace Media2Ns = MediaNamespaceV2;
    private static readonly XNamespace PtzNs = PtzNamespaceV20;
    private static readonly XNamespace SchemaNs = "http://www.onvif.org/ver10/schema";

    private readonly SmartPanelDbContext _db;
    private readonly IDataProtector _protector;
    private readonly CameraTranscodingOptions _transcodingOptions;
    private readonly MediaMtxService _mediaMtxService;
    private readonly ILogger<DirectCameraService> _logger;
    private string? _resolvedFfmpegPath;
    private bool _resolvedFfmpegPathComputed;

    public DirectCameraService(
        SmartPanelDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<CameraTranscodingOptions> transcodingOptions,
        MediaMtxService mediaMtxService,
        ILogger<DirectCameraService> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("SmartPanel.DirectCameras.Credentials.v1");
        _transcodingOptions = transcodingOptions.Value ?? new CameraTranscodingOptions();
        _mediaMtxService = mediaMtxService;
        _logger = logger;
    }

    public bool IsDirectCameraId(string entityId) =>
        !string.IsNullOrWhiteSpace(entityId) &&
        entityId.StartsWith(DirectPrefix, StringComparison.OrdinalIgnoreCase);

    public string? TryGetCameraId(string entityId)
    {
        if (!IsDirectCameraId(entityId))
        {
            return null;
        }

        var id = entityId[DirectPrefix.Length..].Trim();
        return id.Length == 0 ? null : id;
    }

    public async Task<List<DirectCameraDto>> GetDirectCamerasAsync(CancellationToken cancellationToken = default)
    {
        await TrySyncMediaMtxAsync(cancellationToken);

        var entities = await _db.DirectCameras
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entities
            .Select(MapDirectCamera)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<DirectCameraDiscoveryDto>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var matches = await ProbeAsync(cancellationToken);
        var results = new List<DirectCameraDiscoveryDto>();

        foreach (var match in matches
                     .Where(x => x.DeviceServiceUrl is not null)
                     .GroupBy(x => x.DeviceServiceUrl!, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var dto = new DirectCameraDiscoveryDto
            {
                DiscoveryId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(match.DeviceServiceUrl!))).ToLowerInvariant()[..16],
                Host = match.Host,
                Port = match.Port,
                DeviceServiceUrl = match.DeviceServiceUrl!,
                Name = !string.IsNullOrWhiteSpace(match.Name) ? match.Name! : match.Host,
                Hardware = match.Hardware,
                Types = match.Types,
                Scopes = match.Scopes
            };

            var info = await TryGetDeviceInformationAsync(match.DeviceServiceUrl!, null, null, cancellationToken);
            dto.Manufacturer = info?.Manufacturer;
            dto.Model = info?.Model;
            results.Add(dto);
        }

        return results
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DirectCameraDto> SaveManualCameraAsync(DirectCameraSaveRequestDto request, CancellationToken cancellationToken = default)
    {
        var existing = await FindCameraEntityAsync(request.CameraId, cancellationToken);
        var username = NormalizeOptional(request.Username) ?? NormalizeOptional(existing?.Username);
        var password = !string.IsNullOrWhiteSpace(request.Password)
            ? request.Password
            : Unprotect(existing?.PasswordProtected);
        var preferredProfileToken = NormalizeOptional(request.PreferredProfileToken) ?? NormalizeOptional(existing?.SelectedProfileToken);
        var preferredLiveProfileToken = NormalizeOptional(request.PreferredLiveProfileToken) ?? NormalizeOptional(existing?.PreferredLiveProfileToken);

        var description = await DescribeCameraFromRequestAsync(request, username, password, preferredProfileToken, preferredLiveProfileToken, cancellationToken);
        var displayName = NormalizeOptional(request.Name)
            ?? NormalizeOptional(existing?.Name)
            ?? description.Name
            ?? description.Host;

        var entity = existing ?? new DirectCameraEntity
        {
            CameraId = string.IsNullOrWhiteSpace(request.CameraId) ? Guid.NewGuid().ToString("N") : request.CameraId.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        entity.Name = displayName;
        entity.Host = description.Host;
        entity.Port = description.Port;
        entity.Scheme = description.Scheme;
        entity.DeviceServiceUrl = description.DeviceServiceUrl;
        entity.MediaServiceUrl = description.MediaServiceUrl;
        entity.MediaNamespace = description.MediaNamespace;
        entity.PtzServiceUrl = description.PtzServiceUrl;
        entity.SnapshotUri = description.SnapshotUri;
        entity.StreamUri = description.StreamUri;
        entity.SnapshotUrlOverride = NormalizeOptional(request.SnapshotUrlOverride);
        entity.MjpegUrlOverride = NormalizeOptional(request.MjpegUrlOverride);
        entity.SelectedProfileToken = description.SelectedProfileToken;
        entity.PreferredLiveProfileToken = preferredLiveProfileToken;
        entity.LiveProfileToken = description.LiveProfileToken;
        entity.LiveSnapshotUri = description.LiveSnapshotUri;
        entity.LiveStreamUri = description.LiveStreamUri;
        entity.Username = username;
        entity.PasswordProtected = string.IsNullOrWhiteSpace(password) ? null : Protect(password);
        entity.Manufacturer = description.Manufacturer;
        entity.Model = description.Model;
        entity.FirmwareVersion = description.FirmwareVersion;
        entity.SerialNumber = description.SerialNumber;
        entity.HardwareId = description.HardwareId;
        entity.ProfilesJson = JsonSerializer.Serialize(description.Profiles, JsonOptions);
        entity.CapabilitiesJson = JsonSerializer.Serialize(description.Capabilities, JsonOptions);
        entity.Online = true;
        entity.LastError = null;
        entity.LastSeenUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (existing is null)
        {
            _db.DirectCameras.Add(entity);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await TrySyncMediaMtxAsync(cancellationToken);
        return MapDirectCamera(entity);
    }

    public async Task<DirectCameraDto?> RefreshCameraAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        var entity = await FindCameraEntityAsync(cameraId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var username = NormalizeOptional(entity.Username);
        var password = Unprotect(entity.PasswordProtected);

        try
        {
            var description = await DescribeCameraAsync(
                entity.DeviceServiceUrl,
                username,
                password,
                NormalizeOptional(entity.SelectedProfileToken),
                NormalizeOptional(entity.PreferredLiveProfileToken),
                cancellationToken);

            entity.Host = description.Host;
            entity.Port = description.Port;
            entity.Scheme = description.Scheme;
            entity.MediaServiceUrl = description.MediaServiceUrl;
            entity.MediaNamespace = description.MediaNamespace;
            entity.PtzServiceUrl = description.PtzServiceUrl;
            entity.SnapshotUri = description.SnapshotUri;
            entity.StreamUri = description.StreamUri;
            entity.SelectedProfileToken = description.SelectedProfileToken;
            entity.LiveProfileToken = description.LiveProfileToken;
            entity.LiveSnapshotUri = description.LiveSnapshotUri;
            entity.LiveStreamUri = description.LiveStreamUri;
            entity.Manufacturer = description.Manufacturer;
            entity.Model = description.Model;
            entity.FirmwareVersion = description.FirmwareVersion;
            entity.SerialNumber = description.SerialNumber;
            entity.HardwareId = description.HardwareId;
            entity.ProfilesJson = JsonSerializer.Serialize(description.Profiles, JsonOptions);
            entity.CapabilitiesJson = JsonSerializer.Serialize(description.Capabilities, JsonOptions);
            entity.Online = true;
            entity.LastError = null;
            entity.LastSeenUtc = DateTime.UtcNow;
            entity.UpdatedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            entity.Online = false;
            entity.LastError = ex.Message;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            _logger.LogWarning(ex, "Direct camera refresh failed for {CameraId}", cameraId);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await TrySyncMediaMtxAsync(cancellationToken);
        return MapDirectCamera(entity);
    }

    public async Task DeleteCameraAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        var entity = await FindCameraEntityAsync(cameraId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        _db.DirectCameras.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await TrySyncMediaMtxAsync(cancellationToken);
    }

    public async Task<(byte[] Bytes, string ContentType)> GetSnapshotAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        var entity = await RequireCameraEntityAsync(cameraId, cancellationToken);

        try
        {
            return await GetSnapshotResultAsync(entity, cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            await RefreshCameraAsync(cameraId, cancellationToken);
            entity = await RequireCameraEntityAsync(cameraId, cancellationToken);
            return await GetSnapshotResultAsync(entity, cancellationToken);
        }
    }

    public async Task<string> GetLiveViewContentTypeAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        var entity = await RequireCameraEntityAsync(cameraId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(entity.MjpegUrlOverride))
        {
            var username = NormalizeOptional(entity.Username);
            var password = Unprotect(entity.PasswordProtected);
            using var client = CreateHttpClient(username, password);
            using var response = await client.GetAsync(entity.MjpegUrlOverride, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var details = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"MJPEG request failed with {(int)response.StatusCode} ({response.ReasonPhrase}). {details}".Trim());
            }

            return response.Content.Headers.ContentType?.ToString() ?? "multipart/x-mixed-replace";
        }

        if (!HasEffectiveSnapshotSource(entity))
        {
            if (HasRtspTranscodingFallback(entity))
            {
                return "multipart/x-mixed-replace; boundary=frame";
            }

            throw new InvalidOperationException(BuildMissingLiveViewMessage(entity));
        }

        return "multipart/x-mixed-replace; boundary=frame";
    }

    public async Task StreamLiveViewAsync(string cameraId, Stream destination, CancellationToken cancellationToken = default)
    {
        var entity = await RequireCameraEntityAsync(cameraId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(entity.MjpegUrlOverride))
        {
            var username = NormalizeOptional(entity.Username);
            var password = Unprotect(entity.PasswordProtected);
            using var client = CreateHttpClient(username, password);
            using var response = await client.GetAsync(entity.MjpegUrlOverride, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var details = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"MJPEG request failed with {(int)response.StatusCode} ({response.ReasonPhrase}). {details}".Trim());
            }

            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await sourceStream.CopyToAsync(destination, cancellationToken);
            return;
        }

        if (!HasEffectiveSnapshotSource(entity))
        {
            if (HasRtspTranscodingFallback(entity))
            {
                await StreamRtspLiveViewAsync(entity, destination, cancellationToken);
                return;
            }

            throw new InvalidOperationException(BuildMissingLiveViewMessage(entity));
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = await GetSnapshotResultAsync(entity, cancellationToken);
            await WriteMultipartFrameAsync(destination, snapshot.Bytes, snapshot.ContentType, cancellationToken);
        }
    }

    public async Task ControlCameraAsync(string cameraId, CameraControlRequestDto request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireCameraEntityAsync(cameraId, cancellationToken);
        var capabilities = DeserializeList(entity.CapabilitiesJson);

        if (!capabilities.Contains("ptz", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This direct camera does not expose ONVIF PTZ control.");
        }

        if (string.IsNullOrWhiteSpace(entity.PtzServiceUrl) || string.IsNullOrWhiteSpace(entity.SelectedProfileToken))
        {
            throw new InvalidOperationException("PTZ service metadata is missing for this direct camera.");
        }

        var username = NormalizeOptional(entity.Username);
        var password = Unprotect(entity.PasswordProtected);
        var cmd = (request.Command ?? string.Empty).Trim().ToLowerInvariant();
        var step = Math.Clamp(request.Step ?? 0.45, 0.05, 1.0);

        switch (cmd)
        {
            case "left":
                await ContinuousMoveAsync(entity.PtzServiceUrl, entity.SelectedProfileToken, username, password, -step, 0, 0, cancellationToken);
                break;
            case "right":
                await ContinuousMoveAsync(entity.PtzServiceUrl, entity.SelectedProfileToken, username, password, step, 0, 0, cancellationToken);
                break;
            case "up":
                await ContinuousMoveAsync(entity.PtzServiceUrl, entity.SelectedProfileToken, username, password, 0, step, 0, cancellationToken);
                break;
            case "down":
                await ContinuousMoveAsync(entity.PtzServiceUrl, entity.SelectedProfileToken, username, password, 0, -step, 0, cancellationToken);
                break;
            case "zoom_in":
                await ContinuousMoveAsync(entity.PtzServiceUrl, entity.SelectedProfileToken, username, password, 0, 0, step, cancellationToken);
                break;
            case "zoom_out":
                await ContinuousMoveAsync(entity.PtzServiceUrl, entity.SelectedProfileToken, username, password, 0, 0, -step, cancellationToken);
                break;
            case "stop":
                await StopAsync(entity.PtzServiceUrl, entity.SelectedProfileToken, username, password, cancellationToken);
                break;
            case "home":
                await GotoHomePositionAsync(entity.PtzServiceUrl, entity.SelectedProfileToken, username, password, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported direct camera command: {request.Command}");
        }
    }

    private async Task<DirectCameraEntity?> FindCameraEntityAsync(string? cameraId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeOptional(cameraId);
        if (normalized is null)
        {
            return null;
        }

        return await _db.DirectCameras.FirstOrDefaultAsync(x => x.CameraId == normalized, cancellationToken);
    }

    private async Task<DirectCameraEntity> RequireCameraEntityAsync(string cameraId, CancellationToken cancellationToken)
    {
        return await FindCameraEntityAsync(cameraId, cancellationToken)
            ?? throw new InvalidOperationException($"Direct camera '{cameraId}' was not found.");
    }

    private DirectCameraDto MapDirectCamera(DirectCameraEntity entity)
    {
        var entityId = $"{DirectPrefix}{entity.CameraId}";
        var snapshotUrl = CanServeSnapshotEndpoint(entity)
            ? $"/api/cameras/snapshot?entityId={Uri.EscapeDataString(entityId)}"
            : string.Empty;
        var streamUrl = CanServeLiveViewEndpoint(entity)
            ? $"/api/cameras/stream?entityId={Uri.EscapeDataString(entityId)}"
            : string.Empty;
        var webRtcPlayerUrl = _mediaMtxService.GetWebRtcPlayerUrl(entity);
        var hlsPlayerUrl = _mediaMtxService.GetHlsPlayerUrl(entity);

        return new DirectCameraDto
        {
            CameraId = entity.CameraId,
            EntityId = entityId,
            Name = entity.Name,
            Host = entity.Host,
            Port = entity.Port,
            Scheme = entity.Scheme,
            DeviceServiceUrl = entity.DeviceServiceUrl,
            Manufacturer = entity.Manufacturer,
            Model = entity.Model,
            FirmwareVersion = entity.FirmwareVersion,
            SerialNumber = entity.SerialNumber,
            HardwareId = entity.HardwareId,
            Username = entity.Username,
            HasPassword = !string.IsNullOrWhiteSpace(entity.PasswordProtected),
            Online = entity.Online,
            SnapshotUrlOverride = entity.SnapshotUrlOverride,
            MjpegUrlOverride = entity.MjpegUrlOverride,
            SnapshotUrl = snapshotUrl,
            StreamUrl = streamUrl,
            WebRtcPlayerUrl = webRtcPlayerUrl,
            HlsPlayerUrl = hlsPlayerUrl,
            RawStreamUrl = entity.LiveStreamUri ?? entity.StreamUri ?? string.Empty,
            SelectedProfileToken = entity.SelectedProfileToken,
            PreferredLiveProfileToken = entity.PreferredLiveProfileToken,
            LiveProfileToken = entity.LiveProfileToken,
            Profiles = DeserializeProfiles(entity.ProfilesJson),
            Capabilities = BuildEffectiveCapabilities(entity),
            LastError = entity.LastError,
            LastSeenAt = entity.LastSeenUtc.ToLocalTime(),
            UpdatedAt = entity.UpdatedAtUtc.ToLocalTime()
        };
    }

    private async Task<(byte[] Bytes, string ContentType)> GetSnapshotResultAsync(
        DirectCameraEntity entity,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        if (HasEffectiveSnapshotSource(entity))
        {
            try
            {
                return await DownloadSnapshotAsync(entity, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                _logger.LogDebug(ex, "Snapshot source failed for direct camera {CameraId}", entity.CameraId);
            }
        }

        if (HasRtspTranscodingFallback(entity))
        {
            return await CaptureRtspSnapshotAsync(entity, cancellationToken);
        }

        throw lastError ?? new InvalidOperationException(BuildMissingSnapshotMessage(entity));
    }

    private async Task<(byte[] Bytes, string ContentType)> DownloadSnapshotAsync(DirectCameraEntity entity, CancellationToken cancellationToken)
    {
        var snapshotSourceUrl = GetEffectiveSnapshotSourceUrl(entity);
        if (string.IsNullOrWhiteSpace(snapshotSourceUrl))
        {
            throw new InvalidOperationException("This direct camera does not expose a snapshot source.");
        }

        var username = NormalizeOptional(entity.Username);
        var password = Unprotect(entity.PasswordProtected);
        using var client = CreateHttpClient(username, password);
        using var response = await client.GetAsync(snapshotSourceUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Snapshot request failed with {(int)response.StatusCode} ({response.ReasonPhrase}). {details}".Trim());
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        return (bytes, contentType);
    }

    private async Task<CameraDescription> DescribeCameraFromRequestAsync(
        DirectCameraSaveRequestDto request,
        string? username,
        string? password,
        string? preferredProfileToken,
        string? preferredLiveProfileToken,
        CancellationToken cancellationToken)
    {
        var explicitUrl = NormalizeOptional(request.DeviceServiceUrl);
        if (explicitUrl is not null)
        {
            return await DescribeCameraAsync(explicitUrl, username, password, preferredProfileToken, preferredLiveProfileToken, cancellationToken);
        }

        var host = NormalizeOptional(request.Host)
            ?? throw new InvalidOperationException("Host or DeviceServiceUrl is required for a direct camera.");
        var scheme = request.UseTls ? "https" : "http";
        var port = request.Port ?? (request.UseTls ? 443 : 80);
        Exception? lastError = null;

        foreach (var candidate in BuildCandidateDeviceServiceUrls(host, port, scheme))
        {
            try
            {
                return await DescribeCameraAsync(candidate, username, password, preferredProfileToken, preferredLiveProfileToken, cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogDebug(ex, "Unable to describe direct camera via {Candidate}", candidate);
            }
        }

        throw new InvalidOperationException(
            $"Unable to reach an ONVIF device service on {host}:{port}. Last error: {lastError?.Message}");
    }

    private async Task<CameraDescription> DescribeCameraAsync(
        string deviceServiceUrl,
        string? username,
        string? password,
        string? preferredProfileToken,
        string? preferredLiveProfileToken,
        CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(deviceServiceUrl, username, password, cancellationToken);
        var mediaServiceUrl = capabilities.MediaServiceUrl
            ?? throw new InvalidOperationException("The camera does not expose an ONVIF media service.");
        var mediaNamespace = capabilities.MediaNamespace
            ?? throw new InvalidOperationException("The camera media namespace could not be determined.");

        var deviceInfo = await TryGetDeviceInformationAsync(deviceServiceUrl, username, password, cancellationToken);
        var profiles = await GetProfilesAsync(mediaServiceUrl, mediaNamespace, username, password, cancellationToken);
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("The camera returned no ONVIF media profiles.");
        }

        var selectedProfile = SelectControlProfile(profiles, preferredProfileToken);
        var liveProfile = SelectLiveProfile(profiles, preferredLiveProfileToken, selectedProfile);
        var snapshotUri = await TryGetSnapshotUriAsync(mediaServiceUrl, mediaNamespace, selectedProfile.Token, username, password, cancellationToken);
        var streamUri = await TryGetStreamUriAsync(mediaServiceUrl, mediaNamespace, selectedProfile.Token, username, password, cancellationToken);
        var liveSnapshotUri = liveProfile.Token.Equals(selectedProfile.Token, StringComparison.OrdinalIgnoreCase)
            ? snapshotUri
            : await TryGetSnapshotUriAsync(mediaServiceUrl, mediaNamespace, liveProfile.Token, username, password, cancellationToken);
        var liveStreamUri = liveProfile.Token.Equals(selectedProfile.Token, StringComparison.OrdinalIgnoreCase)
            ? streamUri
            : await TryGetStreamUriAsync(mediaServiceUrl, mediaNamespace, liveProfile.Token, username, password, cancellationToken);
        var deviceUri = new Uri(deviceServiceUrl);

        var capabilityList = new List<string>();
        if (!string.IsNullOrWhiteSpace(liveSnapshotUri))
        {
            capabilityList.Add("snapshot");
            capabilityList.Add("stream");
        }

        if (!string.IsNullOrWhiteSpace(liveStreamUri))
        {
            capabilityList.Add("rtsp");
        }

        if (!string.IsNullOrWhiteSpace(capabilities.PtzServiceUrl) && selectedProfile.SupportsPtz)
        {
            capabilityList.Add("ptz");
            capabilityList.Add("stop");
            capabilityList.Add("home");
        }

        return new CameraDescription(
            NormalizeOptional(deviceInfo?.Model)
                ?? NormalizeOptional(deviceInfo?.HardwareId)
                ?? deviceUri.Host,
            deviceUri.Host,
            deviceUri.IsDefaultPort ? (deviceUri.Scheme == "https" ? 443 : 80) : deviceUri.Port,
            deviceUri.Scheme,
            deviceServiceUrl,
            mediaServiceUrl,
            mediaNamespace,
            capabilities.PtzServiceUrl,
            snapshotUri,
            streamUri,
            selectedProfile.Token,
            liveProfile.Token,
            liveSnapshotUri,
            liveStreamUri,
            profiles.Select(x => new DirectCameraProfileDto
            {
                Token = x.Token,
                Name = x.Name,
                SupportsPtz = x.SupportsPtz
            }).ToList(),
            capabilityList.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            deviceInfo?.Manufacturer,
            deviceInfo?.Model,
            deviceInfo?.FirmwareVersion,
            deviceInfo?.SerialNumber,
            deviceInfo?.HardwareId);
    }

    private async Task<OnvifCapabilities> GetCapabilitiesAsync(
        string deviceServiceUrl,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var document = await PostSoapAsync(
            deviceServiceUrl,
            DeviceNs,
            "GetCapabilities",
            new[]
            {
                new XElement(DeviceNs + "Category", "All")
            },
            username,
            password,
            cancellationToken);

        var capabilitiesElement = FindFirstDescendant(document, "Capabilities")
            ?? throw new InvalidOperationException("The ONVIF device capabilities response was empty.");
        var mediaElement = FindFirstChild(capabilitiesElement, "Media");
        var media2Element = FindFirstChild(capabilitiesElement, "Media2");
        var ptzElement = FindFirstChild(capabilitiesElement, "PTZ");

        var mediaV1Url = FirstChildValue(mediaElement, "XAddr");
        var mediaV2Url = FirstChildValue(media2Element, "XAddr");

        return new OnvifCapabilities(
            mediaV1Url ?? mediaV2Url,
            mediaV1Url is not null ? MediaNamespaceV1 : mediaV2Url is not null ? MediaNamespaceV2 : null,
            FirstChildValue(ptzElement, "XAddr"));
    }

    private async Task<OnvifDeviceInformation?> TryGetDeviceInformationAsync(
        string deviceServiceUrl,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await PostSoapAsync(
                deviceServiceUrl,
                DeviceNs,
                "GetDeviceInformation",
                [],
                username,
                password,
                cancellationToken);

            var response = FindFirstDescendant(document, "GetDeviceInformationResponse");
            if (response is null)
            {
                return null;
            }

            return new OnvifDeviceInformation(
                FirstChildValue(response, "Manufacturer"),
                FirstChildValue(response, "Model"),
                FirstChildValue(response, "FirmwareVersion"),
                FirstChildValue(response, "SerialNumber"),
                FirstChildValue(response, "HardwareId"));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetDeviceInformation failed for {DeviceServiceUrl}", deviceServiceUrl);
            return null;
        }
    }

    private async Task<List<OnvifProfile>> GetProfilesAsync(
        string mediaServiceUrl,
        string mediaNamespace,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var bodyNamespace = XNamespace.Get(mediaNamespace);
        var document = await PostSoapAsync(
            mediaServiceUrl,
            bodyNamespace,
            "GetProfiles",
            [],
            username,
            password,
            cancellationToken);

        return document
            .Descendants()
            .Where(x => x.Name.LocalName == "Profiles")
            .Select(x => new OnvifProfile(
                x.Attributes().FirstOrDefault(a => a.Name.LocalName == "token")?.Value ?? string.Empty,
                FirstChildValue(x, "Name") ?? "Profile",
                FindFirstChild(x, "PTZConfiguration") is not null))
            .Where(x => x.Token.Length > 0)
            .ToList();
    }

    private async Task<string?> TryGetSnapshotUriAsync(
        string mediaServiceUrl,
        string mediaNamespace,
        string profileToken,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        try
        {
            var bodyNamespace = XNamespace.Get(mediaNamespace);
            var document = await PostSoapAsync(
                mediaServiceUrl,
                bodyNamespace,
                "GetSnapshotUri",
                new[]
                {
                    new XElement(bodyNamespace + "ProfileToken", profileToken)
                },
                username,
                password,
                cancellationToken);

            return FindFirstDescendant(document, "Uri")?.Value?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetSnapshotUri failed for {MediaServiceUrl}", mediaServiceUrl);
            return null;
        }
    }

    private async Task<string?> TryGetStreamUriAsync(
        string mediaServiceUrl,
        string mediaNamespace,
        string profileToken,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        try
        {
            var bodyNamespace = XNamespace.Get(mediaNamespace);
            var document = await PostSoapAsync(
                mediaServiceUrl,
                bodyNamespace,
                "GetStreamUri",
                new[]
                {
                    new XElement(bodyNamespace + "StreamSetup",
                        new XElement(SchemaNs + "Stream", "RTP-Unicast"),
                        new XElement(SchemaNs + "Transport",
                            new XElement(SchemaNs + "Protocol", "RTSP"))),
                    new XElement(bodyNamespace + "ProfileToken", profileToken)
                },
                username,
                password,
                cancellationToken);

            return FindFirstDescendant(document, "Uri")?.Value?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetStreamUri failed for {MediaServiceUrl}", mediaServiceUrl);
            return null;
        }
    }

    private async Task ContinuousMoveAsync(
        string ptzServiceUrl,
        string profileToken,
        string? username,
        string? password,
        double pan,
        double tilt,
        double zoom,
        CancellationToken cancellationToken)
    {
        var velocity = new XElement(PtzNs + "Velocity");
        if (Math.Abs(pan) > 0.0001 || Math.Abs(tilt) > 0.0001)
        {
            velocity.Add(new XElement(SchemaNs + "PanTilt",
                new XAttribute("x", pan.ToString("0.###", CultureInfo.InvariantCulture)),
                new XAttribute("y", tilt.ToString("0.###", CultureInfo.InvariantCulture))));
        }

        if (Math.Abs(zoom) > 0.0001)
        {
            velocity.Add(new XElement(SchemaNs + "Zoom",
                new XAttribute("x", zoom.ToString("0.###", CultureInfo.InvariantCulture))));
        }

        await PostSoapAsync(
            ptzServiceUrl,
            PtzNs,
            "ContinuousMove",
            new[]
            {
                new XElement(PtzNs + "ProfileToken", profileToken),
                velocity,
                new XElement(PtzNs + "Timeout", "PT3S")
            },
            username,
            password,
            cancellationToken);
    }

    private async Task StopAsync(
        string ptzServiceUrl,
        string profileToken,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        await PostSoapAsync(
            ptzServiceUrl,
            PtzNs,
            "Stop",
            new[]
            {
                new XElement(PtzNs + "ProfileToken", profileToken),
                new XElement(PtzNs + "PanTilt", true),
                new XElement(PtzNs + "Zoom", true)
            },
            username,
            password,
            cancellationToken);
    }

    private async Task GotoHomePositionAsync(
        string ptzServiceUrl,
        string profileToken,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        await PostSoapAsync(
            ptzServiceUrl,
            PtzNs,
            "GotoHomePosition",
            new[]
            {
                new XElement(PtzNs + "ProfileToken", profileToken)
            },
            username,
            password,
            cancellationToken);
    }

    private async Task<XDocument> PostSoapAsync(
        string url,
        XNamespace bodyNamespace,
        string actionName,
        IEnumerable<XElement> bodyChildren,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var body = new XElement(bodyNamespace + actionName, bodyChildren);
        var envelope = new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", Soap),
                new XAttribute(XNamespace.Xmlns + "wsse", Wsse),
                new XAttribute(XNamespace.Xmlns + "wsu", Wsu),
                new XAttribute(XNamespace.Xmlns + "tds", DeviceNs),
                new XAttribute(XNamespace.Xmlns + "trt", MediaNs),
                new XAttribute(XNamespace.Xmlns + "tr2", Media2Ns),
                new XAttribute(XNamespace.Xmlns + "tptz", PtzNs),
                new XAttribute(XNamespace.Xmlns + "tt", SchemaNs),
                BuildSecurityHeader(username, password),
                new XElement(Soap + "Body", body)));

        using var client = CreateHttpClient(username, password);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/soap+xml")
        };

        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            $"application/soap+xml; charset=utf-8; action=\"{bodyNamespace.NamespaceName}/{actionName}\"");

        using var response = await client.SendAsync(request, cancellationToken);
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"ONVIF request {actionName} failed with {(int)response.StatusCode} ({response.ReasonPhrase}). {xml}".Trim());
        }

        var document = XDocument.Parse(xml);
        var fault = FindFirstDescendant(document, "Fault");
        if (fault is not null)
        {
            var reason = FindFirstDescendant(fault, "Text")?.Value
                ?? FindFirstDescendant(fault, "Reason")?.Value
                ?? fault.Value;
            throw new InvalidOperationException($"ONVIF fault during {actionName}: {reason}".Trim());
        }

        return document;
    }

    private XElement? BuildSecurityHeader(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var nonceBytes = RandomNumberGenerator.GetBytes(20);
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var passwordBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        var createdBytes = Encoding.UTF8.GetBytes(created);
        var digestInput = new byte[nonceBytes.Length + createdBytes.Length + passwordBytes.Length];

        Buffer.BlockCopy(nonceBytes, 0, digestInput, 0, nonceBytes.Length);
        Buffer.BlockCopy(createdBytes, 0, digestInput, nonceBytes.Length, createdBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, digestInput, nonceBytes.Length + createdBytes.Length, passwordBytes.Length);

        return new XElement(Soap + "Header",
            new XElement(Wsse + "Security",
                new XAttribute(Soap + "mustUnderstand", "1"),
                new XElement(Wsse + "UsernameToken",
                    new XElement(Wsse + "Username", username),
                    new XElement(Wsse + "Password",
                        new XAttribute("Type",
                            "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"),
                        Convert.ToBase64String(SHA1.HashData(digestInput))),
                    new XElement(Wsse + "Nonce",
                        new XAttribute("EncodingType",
                            "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"),
                        Convert.ToBase64String(nonceBytes)),
                    new XElement(Wsu + "Created", created))));
    }

    private async Task<List<DiscoveredProbeMatch>> ProbeAsync(CancellationToken cancellationToken)
    {
        var results = new List<DiscoveredProbeMatch>();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DiscoveryWindow);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.EnableBroadcast = true;
        client.MulticastLoopback = false;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var probeBytes = Encoding.UTF8.GetBytes(BuildProbeMessage());
        await client.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(IPAddress.Parse(DiscoveryAddress), DiscoveryPort));

        while (!timeoutCts.IsCancellationRequested)
        {
            try
            {
                var response = await client.ReceiveAsync(timeoutCts.Token);
                results.AddRange(ParseProbeMatches(Encoding.UTF8.GetString(response.Buffer), response.RemoteEndPoint.Address));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ONVIF discovery receive failed");
                break;
            }
        }

        return results;
    }

    private static string BuildProbeMessage()
    {
        var messageId = $"uuid:{Guid.NewGuid():D}";
        var envelope = new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", Soap),
                new XAttribute(XNamespace.Xmlns + "a", Wsa),
                new XAttribute(XNamespace.Xmlns + "d", DiscoveryNs),
                new XAttribute(XNamespace.Xmlns + "dn", OnvifDiscovery),
                new XElement(Soap + "Header",
                    new XElement(Wsa + "MessageID", messageId),
                    new XElement(Wsa + "To", "urn:schemas-xmlsoap-org:ws:2005:04:discovery"),
                    new XElement(Wsa + "Action", "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe")),
                new XElement(Soap + "Body",
                    new XElement(DiscoveryNs + "Probe",
                        new XElement(DiscoveryNs + "Types", "dn:NetworkVideoTransmitter")))));

        return envelope.ToString(SaveOptions.DisableFormatting);
    }

    private static List<DiscoveredProbeMatch> ParseProbeMatches(string xml, IPAddress remoteAddress)
    {
        var document = XDocument.Parse(xml);

        return document
            .Descendants()
            .Where(x => x.Name.LocalName == "ProbeMatch")
            .Select(match =>
            {
                var xAddrs = (FindFirstChild(match, "XAddrs")?.Value ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var scopes = (FindFirstChild(match, "Scopes")?.Value ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                var deviceServiceUrl = xAddrs.FirstOrDefault();
                var uri = Uri.TryCreate(deviceServiceUrl, UriKind.Absolute, out var parsed) ? parsed : null;
                var host = uri?.Host ?? remoteAddress.ToString();
                var port = uri is null ? 80 : (uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port);

                return new DiscoveredProbeMatch(
                    deviceServiceUrl,
                    host,
                    port,
                    scopes,
                    Uri.UnescapeDataString(ExtractScope(scopes, "name") ?? string.Empty),
                    Uri.UnescapeDataString(ExtractScope(scopes, "hardware") ?? string.Empty),
                    FindFirstChild(match, "Types")?.Value ?? string.Empty);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.DeviceServiceUrl))
            .ToList();
    }

    private static OnvifProfile SelectControlProfile(IEnumerable<OnvifProfile> profiles, string? preferredProfileToken)
    {
        var list = profiles.ToList();
        if (preferredProfileToken is not null)
        {
            var preferred = list.FirstOrDefault(x => x.Token.Equals(preferredProfileToken, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return list.FirstOrDefault(x => x.SupportsPtz) ?? list.First();
    }

    private static OnvifProfile SelectLiveProfile(
        IEnumerable<OnvifProfile> profiles,
        string? preferredLiveProfileToken,
        OnvifProfile controlProfile)
    {
        var list = profiles.ToList();
        if (preferredLiveProfileToken is not null)
        {
            var preferred = list.FirstOrDefault(x => x.Token.Equals(preferredLiveProfileToken, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return list
            .OrderBy(GetLiveProfileRank)
            .ThenBy(x => x.SupportsPtz ? 1 : 0)
            .ThenBy(x => x.Token.Equals(controlProfile.Token, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? controlProfile;
    }

    private static int GetLiveProfileRank(OnvifProfile profile)
    {
        var haystack = $"{profile.Name} {profile.Token}".Trim().ToLowerInvariant();
        var rank = 100;

        if (ContainsAny(haystack, "sub", "substream", "secondary", "minor", "mobile", "fluent", "extra", "low"))
        {
            rank -= 40;
        }

        if (ContainsAny(haystack, "main", "primary", "major", "high", "hd", "clear"))
        {
            rank += 25;
        }

        if (profile.SupportsPtz)
        {
            rank += 10;
        }

        return rank;
    }

    private static bool ContainsAny(string source, params string[] tokens) =>
        tokens.Any(token => source.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static HttpClient CreateHttpClient(string? username, string? password)
    {
        var handler = new HttpClientHandler
        {
            PreAuthenticate = true,
            UseCookies = false,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        if (!string.IsNullOrWhiteSpace(username))
        {
            handler.Credentials = new NetworkCredential(username, password ?? string.Empty);
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static async Task WriteMultipartFrameAsync(
        Stream destination,
        byte[] frameBytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        var header = $"--frame\r\nContent-Type: {contentType}\r\nContent-Length: {frameBytes.Length}\r\nCache-Control: no-cache\r\n\r\n";
        await destination.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
        await destination.WriteAsync(frameBytes, cancellationToken);
        await destination.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private async Task<(byte[] Bytes, string ContentType)> CaptureRtspSnapshotAsync(
        DirectCameraEntity entity,
        CancellationToken cancellationToken)
    {
        var rtspSourceUrl = GetAuthenticatedRtspSourceUrl(entity)
            ?? throw new InvalidOperationException(BuildMissingSnapshotMessage(entity));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_transcodingOptions.SnapshotTimeoutSeconds, 3, 60)));
        using var process = StartFfmpegProcess(BuildSnapshotArguments(rtspSourceUrl));
        var errorTask = process.StandardError.ReadToEndAsync();
        await using var output = new MemoryStream();

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output, timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            var stderr = await ReadProcessErrorAsync(errorTask);
            throw new InvalidOperationException(
                $"ffmpeg snapshot capture timed out for direct camera '{entity.Name}'. {TrimProcessMessage(stderr)}",
                ex);
        }
        finally
        {
            await EnsureProcessExitedAsync(process);
        }

        var bytes = output.ToArray();
        var errors = await ReadProcessErrorAsync(errorTask);
        if (bytes.Length == 0 || process.ExitCode != 0)
        {
            throw CreateFfmpegFailure("snapshot capture", entity, process.ExitCode, errors);
        }

        return (bytes, "image/jpeg");
    }

    private async Task StreamRtspLiveViewAsync(
        DirectCameraEntity entity,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var rtspSourceUrl = GetAuthenticatedRtspSourceUrl(entity)
            ?? throw new InvalidOperationException(BuildMissingLiveViewMessage(entity));
        using var process = StartFfmpegProcess(BuildLiveStreamArguments(rtspSourceUrl));
        var errorTask = process.StandardError.ReadToEndAsync();
        var emittedFrames = 0L;

        try
        {
            emittedFrames = await StreamJpegPipeAsMultipartAsync(
                process.StandardOutput.BaseStream,
                destination,
                cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            return;
        }
        finally
        {
            await EnsureProcessExitedAsync(process);
        }

        var errors = await ReadProcessErrorAsync(errorTask);
        if (emittedFrames == 0)
        {
            throw CreateFfmpegFailure("live stream", entity, process.ExitCode, errors);
        }

        if (process.ExitCode != 0)
        {
            _logger.LogWarning(
                "ffmpeg RTSP live stream exited with code {ExitCode} after {FrameCount} frame(s) for direct camera {CameraId}. {Details}",
                process.ExitCode,
                emittedFrames,
                entity.CameraId,
                TrimProcessMessage(errors));
        }
    }

    private async Task<long> StreamJpegPipeAsMultipartAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        await using var frameBuffer = new MemoryStream();
        var frameCount = 0L;
        var previousByte = -1;
        var insideFrame = false;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                for (var index = 0; index < read; index++)
                {
                    var currentByte = rented[index];
                    if (!insideFrame)
                    {
                        if (previousByte == 0xFF && currentByte == 0xD8)
                        {
                            frameBuffer.SetLength(0);
                            frameBuffer.WriteByte(0xFF);
                            frameBuffer.WriteByte(0xD8);
                            insideFrame = true;
                        }
                    }
                    else
                    {
                        frameBuffer.WriteByte(currentByte);
                        if (previousByte == 0xFF && currentByte == 0xD9)
                        {
                            await WriteMultipartFrameAsync(destination, frameBuffer.ToArray(), "image/jpeg", cancellationToken);
                            frameBuffer.SetLength(0);
                            insideFrame = false;
                            frameCount++;
                        }
                    }

                    previousByte = currentByte;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return frameCount;
    }

    private Process StartFfmpegProcess(IEnumerable<string> arguments)
    {
        var ffmpegPath = TryGetFfmpegExecutablePath()
            ?? throw new InvalidOperationException(
                "ffmpeg was not found on the SmartPanel host. Install ffmpeg or set CameraTranscoding:FfmpegPath.");
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException("ffmpeg could not be started.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Unable to start ffmpeg from '{ffmpegPath}'. {ex.Message}",
                ex);
        }
    }

    private IEnumerable<string> BuildSnapshotArguments(string rtspSourceUrl)
    {
        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "error";
        yield return "-nostdin";
        yield return "-rtsp_transport";
        yield return GetRtspTransport();
        yield return "-i";
        yield return rtspSourceUrl;
        yield return "-an";
        yield return "-sn";
        yield return "-dn";
        yield return "-frames:v";
        yield return "1";
        yield return "-f";
        yield return "image2pipe";
        yield return "-vcodec";
        yield return "mjpeg";
        yield return "pipe:1";
    }

    private IEnumerable<string> BuildLiveStreamArguments(string rtspSourceUrl)
    {
        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "error";
        yield return "-nostdin";
        yield return "-rtsp_transport";
        yield return GetRtspTransport();
        yield return "-fflags";
        yield return "nobuffer+discardcorrupt";
        yield return "-flags";
        yield return "low_delay";
        yield return "-probesize";
        yield return "32";
        yield return "-analyzeduration";
        yield return "0";
        yield return "-max_delay";
        yield return "0";
        yield return "-i";
        yield return rtspSourceUrl;
        yield return "-an";
        yield return "-sn";
        yield return "-dn";
        yield return "-r";
        yield return Math.Clamp(_transcodingOptions.MjpegFrameRate, 1, 15).ToString(CultureInfo.InvariantCulture);
        yield return "-q:v";
        yield return Math.Clamp(_transcodingOptions.MjpegQuality, 2, 15).ToString(CultureInfo.InvariantCulture);
        yield return "-f";
        yield return "image2pipe";
        yield return "-vcodec";
        yield return "mjpeg";
        yield return "pipe:1";
    }

    private string GetRtspTransport()
    {
        var configured = NormalizeOptional(_transcodingOptions.RtspTransport);
        return string.Equals(configured, "udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp";
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

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return sourceUrl;
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
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

    private string? TryGetFfmpegExecutablePath()
    {
        if (_resolvedFfmpegPathComputed)
        {
            return _resolvedFfmpegPath;
        }

        _resolvedFfmpegPathComputed = true;
        if (!_transcodingOptions.Enabled)
        {
            return null;
        }

        var configured = NormalizeOptional(_transcodingOptions.FfmpegPath) ?? "ffmpeg";
        _resolvedFfmpegPath = ResolveExecutablePath(configured);
        if (_resolvedFfmpegPath is null)
        {
            _logger.LogDebug("ffmpeg executable '{ConfiguredPath}' was not found on PATH.", configured);
        }

        return _resolvedFfmpegPath;
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

    private static async Task<string> ReadProcessErrorAsync(Task<string> errorTask)
    {
        try
        {
            return await errorTask;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task EnsureProcessExitedAsync(Process process)
    {
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
    }

    private static void KillProcessTree(Process process)
    {
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
    }

    private static InvalidOperationException CreateFfmpegFailure(
        string operation,
        DirectCameraEntity entity,
        int exitCode,
        string errorOutput)
    {
        var detail = TrimProcessMessage(errorOutput);
        return new InvalidOperationException(
            $"ffmpeg {operation} failed for direct camera '{entity.Name}' (exit code {exitCode}). {detail}");
    }

    private static string TrimProcessMessage(string? message)
    {
        var normalized = NormalizeOptional(message);
        return normalized ?? "ffmpeg produced no additional error output.";
    }

    private static string BuildMissingSnapshotMessage(DirectCameraEntity entity)
    {
        return string.IsNullOrWhiteSpace(entity.LiveStreamUri) && string.IsNullOrWhiteSpace(entity.StreamUri)
            ? "This direct camera does not expose a snapshot source."
            : "This direct camera does not expose a browser-safe snapshot source. Configure a snapshot URL override or install ffmpeg for RTSP capture.";
    }

    private static string BuildMissingLiveViewMessage(DirectCameraEntity entity)
    {
        return string.IsNullOrWhiteSpace(entity.LiveStreamUri) && string.IsNullOrWhiteSpace(entity.StreamUri)
            ? "This direct camera does not expose a browser-safe live view source."
            : "This direct camera only exposes RTSP. Configure a MJPEG/snapshot override or install ffmpeg for browser live view.";
    }

    private static string? GetEffectiveSnapshotSourceUrl(DirectCameraEntity entity) =>
        NormalizeOptional(entity.SnapshotUrlOverride) ?? NormalizeOptional(entity.LiveSnapshotUri) ?? NormalizeOptional(entity.SnapshotUri);

    private static bool HasEffectiveSnapshotSource(DirectCameraEntity entity) =>
        GetEffectiveSnapshotSourceUrl(entity) is not null;

    private bool HasRtspTranscodingFallback(DirectCameraEntity entity) =>
        TryGetFfmpegExecutablePath() is not null && GetAuthenticatedRtspSourceUrl(entity) is not null;

    private bool CanServeSnapshotEndpoint(DirectCameraEntity entity) =>
        HasEffectiveSnapshotSource(entity) || HasRtspTranscodingFallback(entity);

    private bool CanServeLiveViewEndpoint(DirectCameraEntity entity) =>
        NormalizeOptional(entity.MjpegUrlOverride) is not null || HasEffectiveSnapshotSource(entity) || HasRtspTranscodingFallback(entity);

    private List<string> BuildEffectiveCapabilities(DirectCameraEntity entity)
    {
        var capabilities = new HashSet<string>(DeserializeList(entity.CapabilitiesJson), StringComparer.OrdinalIgnoreCase);
        if (CanServeSnapshotEndpoint(entity))
        {
            capabilities.Add("snapshot");
        }

        if (CanServeLiveViewEndpoint(entity))
        {
            capabilities.Add("stream");
        }

        if (!string.IsNullOrWhiteSpace(entity.LiveStreamUri) || !string.IsNullOrWhiteSpace(entity.StreamUri))
        {
            capabilities.Add("rtsp");
        }

        if (HasRtspTranscodingFallback(entity))
        {
            capabilities.Add("rtsp_transcoding");
        }

        if (_mediaMtxService.HasLowLatencyPlayer(entity))
        {
            capabilities.Add("hls");
            capabilities.Add("low_latency");
            capabilities.Add("webrtc");
        }

        return capabilities
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task TrySyncMediaMtxAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _mediaMtxService.SyncAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MediaMTX sync failed.");
        }
    }

    private string? Protect(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : _protector.Protect(value);

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to unprotect direct camera credential.");
            return null;
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static List<string> DeserializeList(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];

    private static List<DirectCameraProfileDto> DeserializeProfiles(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<DirectCameraProfileDto>>(json, JsonOptions) ?? [];

    private static IEnumerable<string> BuildCandidateDeviceServiceUrls(string host, int port, string scheme)
    {
        var normalizedScheme = string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        var baseUrl = $"{normalizedScheme}://{host}:{port}";
        return
        [
            $"{baseUrl}/onvif/device_service",
            $"{baseUrl}/onvif/Device_service",
            $"{baseUrl}/onvif/device_service/"
        ];
    }

    private static XElement? FindFirstDescendant(XContainer container, string localName) =>
        container.Descendants().FirstOrDefault(x => x.Name.LocalName == localName);

    private static XElement? FindFirstChild(XContainer container, string localName) =>
        container.Elements().FirstOrDefault(x => x.Name.LocalName == localName);

    private static string? FirstChildValue(XContainer? container, string localName) =>
        container is null ? null : FindFirstChild(container, localName)?.Value?.Trim();

    private static string? ExtractScope(IEnumerable<string> scopes, string scopeName)
    {
        var prefix = $"onvif://www.onvif.org/{scopeName}/";
        return scopes.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    }

    private sealed record OnvifCapabilities(string? MediaServiceUrl, string? MediaNamespace, string? PtzServiceUrl);
    private sealed record OnvifDeviceInformation(string? Manufacturer, string? Model, string? FirmwareVersion, string? SerialNumber, string? HardwareId);
    private sealed record OnvifProfile(string Token, string Name, bool SupportsPtz);
    private sealed record DiscoveredProbeMatch(string? DeviceServiceUrl, string Host, int Port, List<string> Scopes, string? Name, string? Hardware, string Types);
    private sealed record CameraDescription(
        string Name,
        string Host,
        int Port,
        string Scheme,
        string DeviceServiceUrl,
        string MediaServiceUrl,
        string MediaNamespace,
        string? PtzServiceUrl,
        string? SnapshotUri,
        string? StreamUri,
        string SelectedProfileToken,
        string LiveProfileToken,
        string? LiveSnapshotUri,
        string? LiveStreamUri,
        List<DirectCameraProfileDto> Profiles,
        List<string> Capabilities,
        string? Manufacturer,
        string? Model,
        string? FirmwareVersion,
        string? SerialNumber,
        string? HardwareId);
}
