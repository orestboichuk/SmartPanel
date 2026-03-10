using System.Text.Json.Serialization;

namespace SmartPanel.Api.Services;

public sealed class HomeAssistantRegistrySnapshot
{
    public Dictionary<string, string> RoomByEntityId { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DeviceIdByEntityId { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class HomeAssistantAreaRegistryEntry
{
    [JsonPropertyName("area_id")]
    public string AreaId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class HomeAssistantDeviceRegistryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("area_id")]
    public string? AreaId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("name_by_user")]
    public string? NameByUser { get; set; }
}

public sealed class HomeAssistantEntityRegistryEntry
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [JsonPropertyName("area_id")]
    public string? AreaId { get; set; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }
}
