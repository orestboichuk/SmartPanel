using SmartPanel.Shared;

namespace SmartPanel.Api.Services;

public interface IDirectCameraService
{
    bool IsDirectCameraId(string entityId);
    string? TryGetCameraId(string entityId);
    Task<List<DirectCameraDto>> GetDirectCamerasAsync(CancellationToken cancellationToken = default);
    Task<List<DirectCameraDiscoveryDto>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<DirectCameraDto> SaveManualCameraAsync(DirectCameraSaveRequestDto request, CancellationToken cancellationToken = default);
    Task<DirectCameraDto?> RefreshCameraAsync(string cameraId, CancellationToken cancellationToken = default);
    Task DeleteCameraAsync(string cameraId, CancellationToken cancellationToken = default);
    Task<(byte[] Bytes, string ContentType)> GetSnapshotAsync(string cameraId, CancellationToken cancellationToken = default);
    Task<string> GetLiveViewContentTypeAsync(string cameraId, CancellationToken cancellationToken = default);
    Task StreamLiveViewAsync(string cameraId, Stream destination, CancellationToken cancellationToken = default);
    Task ControlCameraAsync(string cameraId, CameraControlRequestDto request, CancellationToken cancellationToken = default);
}
