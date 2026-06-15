using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppBackup.Configuration;
using WhatsAppBackup.Data;
using WhatsAppBackup.Models;

namespace WhatsAppBackup.Services;

public interface IBackupService
{
    Task<BackupResult> RunBackupAsync(CancellationToken cancellationToken = default);
    Task<BackupResult> RunIncrementalBackupAsync(CancellationToken cancellationToken = default);
}

public class BackupResult
{
    public bool Success { get; set; }
    public int ChatsProcessed { get; set; }
    public int MessagesProcessed { get; set; }
    public int ContactsProcessed { get; set; }
    public int MediaDownloaded { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}

public class BackupService : IBackupService
{
    private readonly ILogger<BackupService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOpenClawClient _openClawClient;
    private readonly BackupSettings _settings;

    public BackupService(
        ILogger<BackupService> logger,
        IServiceScopeFactory scopeFactory,
        IOpenClawClient openClawClient,
        IOptions<BackupSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _openClawClient = openClawClient;
        _settings = settings.Value;
    }

    public async Task<BackupResult> RunBackupAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new BackupResult();

        try
        {
            _logger.LogInformation("Starting full WhatsApp backup...");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WhatsAppDbContext>();

            // Ensure database is created and migrated
            await db.Database.MigrateAsync(cancellationToken);

            // Get all chats from OpenClaw
            var chats = await _openClawClient.GetChatsAsync(cancellationToken);
            
            foreach (var chatData in chats)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var chat = await ProcessChatAsync(db, chatData, cancellationToken);
                result.ChatsProcessed++;

                // Get messages for this chat
                var messages = await _openClawClient.GetMessagesAsync(chatData.Jid, cancellationToken);
                
                foreach (var msgData in messages)
                {
                    await ProcessMessageAsync(db, chat, msgData, cancellationToken);
                    result.MessagesProcessed++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            result.Success = true;
            _logger.LogInformation(
                "Backup completed: {Chats} chats, {Messages} messages in {Duration:F1}s",
                result.ChatsProcessed, result.MessagesProcessed, 
                (DateTime.UtcNow - startTime).TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed");
            result.Success = false;
            result.Error = ex.Message;
        }

        result.Duration = DateTime.UtcNow - startTime;
        return result;
    }

    public async Task<BackupResult> RunIncrementalBackupAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new BackupResult();

        try
        {
            _logger.LogInformation("Starting incremental WhatsApp backup...");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WhatsAppDbContext>();

            // Get the last message timestamp
            var lastBackup = await db.Messages
                .OrderByDescending(m => m.Timestamp)
                .Select(m => m.Timestamp)
                .FirstOrDefaultAsync(cancellationToken);

            _logger.LogInformation("Last backup timestamp: {Timestamp}", lastBackup);

            // Get new messages since last backup
            var chats = await _openClawClient.GetChatsAsync(cancellationToken);

            foreach (var chatData in chats)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chat = await ProcessChatAsync(db, chatData, cancellationToken);
                result.ChatsProcessed++;

                // Get only new messages
                var messages = await _openClawClient.GetMessagesAsync(
                    chatData.Jid, 
                    since: lastBackup,
                    cancellationToken: cancellationToken);

                foreach (var msgData in messages)
                {
                    var isNew = await ProcessMessageAsync(db, chat, msgData, cancellationToken);
                    if (isNew) result.MessagesProcessed++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            result.Success = true;
            _logger.LogInformation(
                "Incremental backup completed: {Messages} new messages in {Duration:F1}s",
                result.MessagesProcessed, (DateTime.UtcNow - startTime).TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incremental backup failed");
            result.Success = false;
            result.Error = ex.Message;
        }

        result.Duration = DateTime.UtcNow - startTime;
        return result;
    }

    private async Task<Chat> ProcessChatAsync(
        WhatsAppDbContext db, 
        ChatData chatData,
        CancellationToken cancellationToken)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(
            c => c.Jid == chatData.Jid, cancellationToken);

        if (chat == null)
        {
            chat = new Chat
            {
                Jid = chatData.Jid,
                ChatType = chatData.IsGroup ? "group" : "dm",
                Name = chatData.Name
            };
            db.Chats.Add(chat);
        }
        else
        {
            chat.Name = chatData.Name ?? chat.Name;
            chat.UpdatedAt = DateTime.UtcNow;
        }

        return chat;
    }

    private async Task<bool> ProcessMessageAsync(
        WhatsAppDbContext db,
        Chat chat,
        MessageData msgData,
        CancellationToken cancellationToken)
    {
        // Check if message already exists
        var exists = await db.Messages.AnyAsync(
            m => m.MessageId == msgData.Id, cancellationToken);

        if (exists) return false;

        // Get or create sender contact
        Contact? sender = null;
        if (!string.IsNullOrEmpty(msgData.SenderPhone))
        {
            sender = await GetOrCreateContactAsync(db, msgData.SenderPhone, msgData.SenderName, cancellationToken);
        }

        var message = new Message
        {
            MessageId = msgData.Id,
            ChatId = chat.Id,
            SenderId = sender?.Id,
            MessageType = msgData.Type,
            Body = msgData.Body,
            Caption = msgData.Caption,
            IsFromMe = msgData.IsFromMe,
            IsForwarded = msgData.IsForwarded,
            Timestamp = msgData.Timestamp,
            Status = msgData.Status ?? "sent"
        };

        db.Messages.Add(message);

        // Update chat's last message time
        if (chat.LastMessageAt == null || msgData.Timestamp > chat.LastMessageAt)
        {
            chat.LastMessageAt = msgData.Timestamp;
        }

        // Process media if present
        if (msgData.Media != null && _settings.EnableMediaDownload)
        {
            await ProcessMediaAsync(db, message, msgData.Media, cancellationToken);
        }

        return true;
    }

    private async Task<Contact> GetOrCreateContactAsync(
        WhatsAppDbContext db,
        string phone,
        string? name,
        CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(
            c => c.Phone == phone, cancellationToken);

        if (contact == null)
        {
            contact = new Contact
            {
                Phone = phone,
                PushName = name
            };
            db.Contacts.Add(contact);
        }
        else if (name != null && contact.PushName != name)
        {
            contact.PushName = name;
            contact.UpdatedAt = DateTime.UtcNow;
        }

        return contact;
    }

    private async Task ProcessMediaAsync(
        WhatsAppDbContext db,
        Message message,
        MediaData mediaData,
        CancellationToken cancellationToken)
    {
        var media = new Media
        {
            MessageId = message.Id,
            MediaType = mediaData.Type,
            MimeType = mediaData.MimeType,
            FileSize = mediaData.FileSize,
            FileName = mediaData.FileName,
            OriginalUrl = mediaData.Url
        };

        // Download media if URL is available
        if (!string.IsNullOrEmpty(mediaData.Url) && _settings.EnableMediaDownload)
        {
            try
            {
                var localPath = await DownloadMediaAsync(mediaData, message.MessageId, cancellationToken);
                media.FilePath = localPath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download media for message {MessageId}", message.MessageId);
            }
        }

        db.Media.Add(media);
    }

    private async Task<string> DownloadMediaAsync(
        MediaData mediaData,
        string messageId,
        CancellationToken cancellationToken)
    {
        var extension = GetExtensionFromMimeType(mediaData.MimeType);
        var fileName = $"{messageId}_{Guid.NewGuid():N}{extension}";
        var subFolder = mediaData.Type.ToLowerInvariant();
        var directory = Path.Combine(_settings.MediaStoragePath, subFolder);
        
        Directory.CreateDirectory(directory);
        
        var filePath = Path.Combine(directory, fileName);

        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(mediaData.Url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var fileStream = File.Create(filePath);
        await response.Content.CopyToAsync(fileStream, cancellationToken);

        return filePath;
    }

    private static string GetExtensionFromMimeType(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "audio/ogg" => ".ogg",
            "audio/mpeg" => ".mp3",
            "application/pdf" => ".pdf",
            _ => ""
        };
    }
}
