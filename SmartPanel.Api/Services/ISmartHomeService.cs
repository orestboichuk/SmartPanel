using SmartPanel.Shared;

namespace SmartPanel.Api.Services;

public interface ISmartHomeService
{
    Task<DashboardDto> GetDashboardAsync();
    Task<List<RoomDto>> GetRoomsAsync();
    Task<List<CameraDto>> GetCamerasAsync();
    Task<List<TvDto>> GetTvsAsync();
    Task<List<SceneDto>> GetScenesAsync();
    Task<SensorDetailsDto?> GetSensorDetailsAsync(string entityId, int hours = 24);
    Task<MotionDebugDto?> GetMotionDebugAsync(string entityId, int hours = 24, int limit = 50);
    Task ControlClimateAsync(ClimateControlRequestDto request);
    Task ControlTvAsync(TvControlRequestDto request);
    Task ControlCameraAsync(CameraControlRequestDto request);
    Task ActivateSceneAsync(string sceneName);
    Task AssignDeviceToRoomAsync(string entityId, string roomName);
    Task<DeviceDisplayNameDto?> UpdateDeviceDisplayNameAsync(DeviceDisplayNameRequestDto request);
    Task<DeviceTypeOverrideDto?> UpdateDeviceTypeOverrideAsync(DeviceTypeOverrideRequestDto request);
    Task<List<LightDto>> GetLightsAsync();
    Task ControlLightAsync(LightControlRequestDto request);
    Task<List<ClimateDto>> GetClimateDevicesAsync();
    Task ClearAllDataAsync();
    Task ClearAllDataAndResyncAsync();
    Task RemoveHaEntityAsync(string entityId);
}
