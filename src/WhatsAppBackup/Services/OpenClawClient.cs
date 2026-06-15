using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WhatsAppBackup.Configuration;

namespace WhatsAppBackup.Services;

public interface IOpenClawClient
{
    Task<GatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<SyncStatus> GetSyncStatusAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatData>> GetChatsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<MessageData>> GetMessagesAsync(string jid, DateTime? since = null, CancellationToken cancellationToken = default);
}

public class SyncStatus
{
    public bool Connected { get; set; }
    public bool SyncComplete { get; set; }
    public int ChatsCount { get; set; }
    public int MessagesCount { get; set; }
}

public class GatewayStatus
{
    public bool IsGatewayReachable { get; set; }
    public bool IsWhatsAppConnected { get; set; }
    public string? Phone { get; set; }
    public string? DisplayName { get; set; }
    public string? State { get; set; }
    public string? QrCode { get; set; }
    public string? Error { get; set; }
}

public class ChatData
{
    public string Jid { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool IsGroup { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

public class MessageData
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string? Body { get; set; }
    public string? Caption { get; set; }
    public string? SenderPhone { get; set; }
    public string? SenderName { get; set; }
    public bool IsFromMe { get; set; }
    public bool IsForwarded { get; set; }
    public string? Status { get; set; }
    public DateTime Timestamp { get; set; }
    public MediaData? Media { get; set; }
}

public class MediaData
{
    public string Type { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public string? Url { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
}

public class OpenClawClient : IOpenClawClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenClawClient> _logger;
    private readonly OpenClawSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    public OpenClawClient(
        HttpClient httpClient,
        ILogger<OpenClawClient> logger,
        IOptions<OpenClawSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _httpClient.BaseAddress = new Uri(_settings.GatewayUrl);
        
        if (!string.IsNullOrEmpty(_settings.GatewayToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.GatewayToken);
        }
    }

    public async Task<GatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new GatewayStatus();
        try
        {
            var response = await _httpClient.GetAsync("/api/whatsapp/status", cancellationToken);
            status.IsGatewayReachable = true;

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
                    cancellationToken: cancellationToken);

                status.State = payload.TryGetProperty("state", out var state) ? state.GetString() : null;
                status.Phone = payload.TryGetProperty("phone", out var phone) ? phone.GetString() : null;
                status.DisplayName = payload.TryGetProperty("name", out var name) ? name.GetString() : null;
                status.QrCode = payload.TryGetProperty("qrCode", out var qr) ? qr.GetString() : null;

                var connected = payload.TryGetProperty("connected", out var conn) && conn.GetBoolean();
                var stateStr = status.State?.ToUpperInvariant();
                status.IsWhatsAppConnected = connected || stateStr == "CONNECTED" || stateStr == "OPEN";
            }
            else
            {
                status.Error = $"Gateway returned {(int)response.StatusCode} {response.ReasonPhrase}";
            }
        }
        catch (HttpRequestException ex)
        {
            status.IsGatewayReachable = false;
            status.Error = ex.Message;
        }
        catch (TaskCanceledException)
        {
            status.IsGatewayReachable = false;
            status.Error = "Connection timed out";
        }

        return status;
    }

    public async Task<SyncStatus> GetSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/whatsapp/sync-status", cancellationToken);
            if (!response.IsSuccessStatusCode) return new SyncStatus();
            return await response.Content.ReadFromJsonAsync<SyncStatus>(cancellationToken: cancellationToken)
                ?? new SyncStatus();
        }
        catch { return new SyncStatus(); }
    }

    public async Task<IEnumerable<ChatData>> GetChatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // This is a placeholder - actual implementation depends on OpenClaw's API
            // You may need to read from WhatsApp's local store or implement a custom endpoint
            _logger.LogInformation("Fetching chats from OpenClaw gateway...");
            
            var response = await _httpClient.GetAsync("/api/whatsapp/chats", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch chats: {Status}", response.StatusCode);
                return Enumerable.Empty<ChatData>();
            }

            var chats = await response.Content.ReadFromJsonAsync<IEnumerable<ChatData>>(
                _jsonOptions, cancellationToken);
            
            return chats ?? Enumerable.Empty<ChatData>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching chats");
            return Enumerable.Empty<ChatData>();
        }
    }

    public async Task<IEnumerable<MessageData>> GetMessagesAsync(
        string jid, 
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching messages for {Jid} since {Since}", jid, since);
            
            var url = $"/api/whatsapp/messages/{Uri.EscapeDataString(jid)}";
            if (since.HasValue)
            {
                url += $"?since={since.Value:O}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch messages for {Jid}: {Status}", jid, response.StatusCode);
                return Enumerable.Empty<MessageData>();
            }

            var messages = await response.Content.ReadFromJsonAsync<IEnumerable<MessageData>>(
                _jsonOptions, cancellationToken);
            
            return messages ?? Enumerable.Empty<MessageData>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching messages for {Jid}", jid);
            return Enumerable.Empty<MessageData>();
        }
    }
}
