using System.Text.Json.Serialization;

namespace SmartPanel.Api.Services;

public class HomeAssistantHistoryStateDto
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("last_changed")]
    public DateTime LastChanged { get; set; }
}
