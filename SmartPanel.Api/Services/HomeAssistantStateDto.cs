using System.Text.Json.Serialization;

namespace SmartPanel.Api.Services;

public class HomeAssistantStateDto
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("attributes")]
    public Dictionary<string, object>? Attributes { get; set; }

    [JsonPropertyName("last_changed")]
    public DateTime? LastChanged { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTime? LastUpdated { get; set; }

    [JsonPropertyName("last_reported")]
    public DateTime? LastReported { get; set; }
}
