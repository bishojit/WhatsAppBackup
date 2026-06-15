using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WhatsAppBackup.Configuration;

namespace WhatsAppBackup.Services;

public class CacheInfo
{
    public DateTime SavedAt { get; set; }
    public int ChatCount { get; set; }
    public int MessageCount { get; set; }
}

public interface ILocalCacheService
{
    string CacheDirectory { get; }
    Task SaveAsync(IEnumerable<ChatData> chats, Dictionary<string, List<MessageData>> messagesByJid);
    Task<CacheInfo?> GetInfoAsync();
}

public class LocalCacheService : ILocalCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<LocalCacheService> _logger;

    public string CacheDirectory { get; }

    public LocalCacheService(IOptions<BackupSettings> settings, ILogger<LocalCacheService> logger)
    {
        CacheDirectory = settings.Value.CacheDirectory;
        _logger = logger;
    }

    public async Task SaveAsync(
        IEnumerable<ChatData> chats,
        Dictionary<string, List<MessageData>> messagesByJid)
    {
        Directory.CreateDirectory(CacheDirectory);

        var chatList = chats.ToList();
        await WriteJsonAsync("chats.json", chatList);

        var messagesDir = Path.Combine(CacheDirectory, "messages");
        Directory.CreateDirectory(messagesDir);

        int totalMessages = 0;
        foreach (var (jid, messages) in messagesByJid)
        {
            totalMessages += messages.Count;
            var file = Path.Combine(messagesDir, $"{HashJid(jid)}.json");
            await File.WriteAllTextAsync(file,
                JsonSerializer.Serialize(new { Jid = jid, Messages = messages }, JsonOptions));
        }

        var info = new CacheInfo
        {
            SavedAt = DateTime.UtcNow,
            ChatCount = chatList.Count,
            MessageCount = totalMessages
        };
        await WriteJsonAsync("cache-info.json", info);

        _logger.LogInformation("Cache saved: {Chats} chats, {Messages} messages → {Dir}",
            chatList.Count, totalMessages, CacheDirectory);
    }

    public async Task<CacheInfo?> GetInfoAsync()
    {
        var path = Path.Combine(CacheDirectory, "cache-info.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<CacheInfo>(json);
    }

    private async Task WriteJsonAsync(string relativePath, object data)
    {
        var fullPath = Path.Combine(CacheDirectory, relativePath);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(data, JsonOptions));
    }

    private static string HashJid(string jid) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jid)))[..16];
}
