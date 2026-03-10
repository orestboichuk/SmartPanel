using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SmartPanel.Api.Services;

public class HomeAssistantClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _token;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HomeAssistantClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        _baseUrl = configuration["HomeAssistant:BaseUrl"] ?? string.Empty;
        _token = configuration["HomeAssistant:Token"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("HomeAssistant:BaseUrl is not configured.");

        if (string.IsNullOrWhiteSpace(_token))
            throw new InvalidOperationException("HomeAssistant:Token is not configured.");

        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _token);
    }

    public async Task<string> GetApiStatusAsync()
    {
        return await _httpClient.GetStringAsync("/api/");
    }

    public async Task<string> GetStatesRawAsync()
    {
        return await _httpClient.GetStringAsync("/api/states");
    }

    public async Task<List<HomeAssistantStateDto>> GetStatesAsync()
    {
        var json = await _httpClient.GetStringAsync("/api/states");
        var result = JsonSerializer.Deserialize<List<HomeAssistantStateDto>>(json, JsonOptions);
        return result ?? new List<HomeAssistantStateDto>();
    }

    public async Task<List<HomeAssistantHistoryStateDto>> GetSensorHistoryAsync(string entityId, int hours = 24)
    {
        var start = DateTime.UtcNow.AddHours(-Math.Clamp(hours, 1, 168));
        var encodedEntityId = WebUtility.UrlEncode(entityId);
        var path = $"/api/history/period/{start:O}?filter_entity_id={encodedEntityId}&minimal_response&no_attributes";

        var json = await _httpClient.GetStringAsync(path);
        var result = JsonSerializer.Deserialize<List<List<HomeAssistantHistoryStateDto>>>(json, JsonOptions);
        return result?.FirstOrDefault() ?? new List<HomeAssistantHistoryStateDto>();
    }

    public async Task<HomeAssistantRegistrySnapshot> GetRegistrySnapshotAsync(CancellationToken cancellationToken = default)
    {
        var wsUrl = _baseUrl.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/') + "/api/websocket";

        using var socket = new ClientWebSocket();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        await socket.ConnectAsync(new Uri(wsUrl), timeoutCts.Token);
        using (await ReceiveAsync(socket, timeoutCts.Token))
        {
            // auth_required
        }

        await SendAsync(socket, new { type = "auth", access_token = _token }, timeoutCts.Token);
        using (var authReply = await ReceiveAsync(socket, timeoutCts.Token))
        {
            var authType = authReply.RootElement.GetProperty("type").GetString();
            if (!string.Equals(authType, "auth_ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Home Assistant websocket authentication failed.");
            }
        }

        var areas = await RequestAsync<List<HomeAssistantAreaRegistryEntry>>(socket, 1, "config/area_registry/list", timeoutCts.Token);
        var devices = await RequestAsync<List<HomeAssistantDeviceRegistryEntry>>(socket, 2, "config/device_registry/list", timeoutCts.Token);
        var entities = await RequestAsync<List<HomeAssistantEntityRegistryEntry>>(socket, 3, "config/entity_registry/list", timeoutCts.Token);

        var areaById = areas
            .Where(x => !string.IsNullOrWhiteSpace(x.AreaId))
            .GroupBy(x => x.AreaId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Name, StringComparer.OrdinalIgnoreCase);

        var deviceById = devices
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        var roomByEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var deviceIdByEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            if (string.IsNullOrWhiteSpace(entity.EntityId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entity.DeviceId))
            {
                deviceIdByEntityId[entity.EntityId] = entity.DeviceId;
            }

            var areaId = entity.AreaId;
            if (string.IsNullOrWhiteSpace(areaId) &&
                !string.IsNullOrWhiteSpace(entity.DeviceId) &&
                deviceById.TryGetValue(entity.DeviceId, out var device))
            {
                areaId = device.AreaId;
            }

            if (string.IsNullOrWhiteSpace(areaId) ||
                !areaById.TryGetValue(areaId, out var roomName) ||
                string.IsNullOrWhiteSpace(roomName))
            {
                continue;
            }

            roomByEntityId[entity.EntityId] = roomName;
        }

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }

        return new HomeAssistantRegistrySnapshot
        {
            RoomByEntityId = roomByEntityId,
            DeviceIdByEntityId = deviceIdByEntityId
        };
    }

    public async Task CallServiceAsync(string domain, string service, object payload)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/services/{domain}/{service}", payload);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var details = await response.Content.ReadAsStringAsync();
        var message = $"Home Assistant service call failed: {domain}.{service} returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        if (!string.IsNullOrWhiteSpace(details))
        {
            message += $" Details: {details}";
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    public Task<HttpResponseMessage> GetCameraSnapshotAsync(string entityId)
    {
        var path = $"/api/camera_proxy/{WebUtility.UrlEncode(entityId)}";
        return _httpClient.GetAsync(path);
    }

    public Task<HttpResponseMessage> GetCameraStreamAsync(string entityId)
    {
        var path = $"/api/camera_proxy_stream/{WebUtility.UrlEncode(entityId)}";
        return _httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
    }

    private static async Task<T> RequestAsync<T>(ClientWebSocket socket, int id, string type, CancellationToken cancellationToken)
    {
        await SendAsync(socket, new { id, type }, cancellationToken);
        using var doc = await ReceiveAsync(socket, cancellationToken);
        var resultJson = doc.RootElement.GetProperty("result").GetRawText();
        return JsonSerializer.Deserialize<T>(resultJson, JsonOptions)
            ?? throw new InvalidOperationException($"Unable to parse Home Assistant response for {type}.");
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<JsonDocument> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Home Assistant websocket closed unexpectedly.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
