using System.Net.Http.Json;
using SmartPanel.Shared;

namespace SmartPanel.Web.Services;

public class SmartHomeApiClient
{
    private readonly HttpClient _httpClient;

    public SmartHomeApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<DashboardDto> GetDashboardAsync()
        => await _httpClient.GetFromJsonAsync<DashboardDto>("api/dashboard") ?? new DashboardDto();

    public async Task<List<RoomDto>> GetRoomsAsync()
        => await _httpClient.GetFromJsonAsync<List<RoomDto>>("api/rooms") ?? [];

    public async Task<List<CameraDto>> GetCamerasAsync()
        => NormalizeCameraUrls(await _httpClient.GetFromJsonAsync<List<CameraDto>>("api/cameras") ?? []);

    public async Task ResyncHaCamerasAsync()
    {
        var response = await _httpClient.PostAsync("api/cameras/ha/resync", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveHaCameraAsync(string entityId)
    {
        var response = await _httpClient.DeleteAsync($"api/cameras/ha/{Uri.EscapeDataString(entityId)}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<DirectCameraDto>> GetDirectCamerasAsync()
        => NormalizeDirectCameraUrls(await _httpClient.GetFromJsonAsync<List<DirectCameraDto>>("api/cameras/direct") ?? []);

    public async Task<List<DirectCameraDiscoveryDto>> DiscoverDirectCamerasAsync()
    {
        var response = await _httpClient.PostAsync("api/cameras/direct/discover", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<DirectCameraDiscoveryDto>>() ?? [];
    }

    public async Task<DirectCameraDto?> SaveDirectCameraAsync(DirectCameraSaveRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/cameras/direct/manual", request);
        response.EnsureSuccessStatusCode();
        return NormalizeDirectCameraUrl(await response.Content.ReadFromJsonAsync<DirectCameraDto>());
    }

    public async Task<DirectCameraDto?> RefreshDirectCameraAsync(string cameraId)
    {
        var response = await _httpClient.PostAsync($"api/cameras/direct/{Uri.EscapeDataString(cameraId)}/refresh", null);
        response.EnsureSuccessStatusCode();
        return NormalizeDirectCameraUrl(await response.Content.ReadFromJsonAsync<DirectCameraDto>());
    }

    public async Task DeleteDirectCameraAsync(string cameraId)
    {
        var response = await _httpClient.DeleteAsync($"api/cameras/direct/{Uri.EscapeDataString(cameraId)}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<ClimateDto>> GetClimateDevicesAsync()
        => await _httpClient.GetFromJsonAsync<List<ClimateDto>>("api/climate") ?? [];

    public async Task ControlClimateDeviceAsync(ClimateControlRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/climate/control", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<LightDto>> GetLightsAsync()
        => await _httpClient.GetFromJsonAsync<List<LightDto>>("api/lights") ?? [];

    public async Task ControlLightAsync(LightControlRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/lights/control", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<TvDto>> GetTvsAsync()
        => await _httpClient.GetFromJsonAsync<List<TvDto>>("api/tvs") ?? [];

    public async Task ControlTvAsync(TvControlRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/tvs/control", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task ControlCameraAsync(CameraControlRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/cameras/control", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<SceneDto>> GetScenesAsync()
        => await _httpClient.GetFromJsonAsync<List<SceneDto>>("api/scenes") ?? [];

    public async Task<SensorDetailsDto?> GetSensorDetailsAsync(string entityId, int hours = 24)
        => await _httpClient.GetFromJsonAsync<SensorDetailsDto>($"api/sensors/details?entityId={Uri.EscapeDataString(entityId)}&hours={hours}");

    public async Task<MotionDebugDto?> GetMotionDebugAsync(string entityId, int hours = 24, int limit = 50)
        => await _httpClient.GetFromJsonAsync<MotionDebugDto>($"api/sensors/motion/debug?entityId={Uri.EscapeDataString(entityId)}&hours={hours}&limit={limit}");

    public async Task<DeviceDisplayNameDto?> UpdateDeviceDisplayNameAsync(DeviceDisplayNameRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/rooms/display-name", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceDisplayNameDto>();
    }

    public async Task<DeviceTypeOverrideDto?> UpdateDeviceTypeAsync(DeviceTypeOverrideRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/rooms/device-type", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceTypeOverrideDto>();
    }

    public async Task ControlClimateAsync(ClimateControlRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/sensors/climate/control", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task ActivateSceneAsync(string sceneName)
    {
        var response = await _httpClient.PostAsync($"api/scenes/{sceneName}/activate", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearAllDataAsync()
    {
        var response = await _httpClient.PostAsync("api/dashboard/clear-data", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearAllDataAndResyncAsync()
    {
        var response = await _httpClient.PostAsync("api/dashboard/clear-and-resync", null);
        response.EnsureSuccessStatusCode();
    }

    private List<CameraDto> NormalizeCameraUrls(List<CameraDto> cameras)
    {
        foreach (var camera in cameras)
        {
            NormalizeCameraUrl(camera);
        }

        return cameras;
    }

    private CameraDto? NormalizeCameraUrl(CameraDto? camera)
    {
        if (camera is null)
        {
            return null;
        }

        camera.SnapshotUrl = ToAbsoluteApiUrl(camera.SnapshotUrl);
        camera.StreamUrl = ToAbsoluteApiUrl(camera.StreamUrl);
        return camera;
    }

    private List<DirectCameraDto> NormalizeDirectCameraUrls(List<DirectCameraDto> cameras)
    {
        foreach (var camera in cameras)
        {
            NormalizeDirectCameraUrl(camera);
        }

        return cameras;
    }

    private DirectCameraDto? NormalizeDirectCameraUrl(DirectCameraDto? camera)
    {
        if (camera is null)
        {
            return null;
        }

        camera.SnapshotUrl = ToAbsoluteApiUrl(camera.SnapshotUrl);
        camera.StreamUrl = ToAbsoluteApiUrl(camera.StreamUrl);
        camera.WebRtcPlayerUrl = ToAbsoluteApiUrl(camera.WebRtcPlayerUrl);
        camera.HlsPlayerUrl = ToAbsoluteApiUrl(camera.HlsPlayerUrl);
        return camera;
    }

    private string ToAbsoluteApiUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url;
        }

        if (_httpClient.BaseAddress is null)
        {
            return url;
        }

        return new Uri(_httpClient.BaseAddress, url).ToString();
    }
}
