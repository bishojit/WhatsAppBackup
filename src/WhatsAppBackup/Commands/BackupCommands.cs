using WhatsAppBackup.Services;

namespace WhatsAppBackup.Commands;

public interface IBackupCommands
{
    Task<int> RunFullBackupAsync();
    Task<int> RunIncrementalBackupAsync();
}

public class BackupCommands : IBackupCommands
{
    private readonly ILogger<BackupCommands> _logger;
    private readonly IBackupService _backupService;

    public BackupCommands(
        ILogger<BackupCommands> logger,
        IBackupService backupService)
    {
        _logger = logger;
        _backupService = backupService;
    }

    public async Task<int> RunFullBackupAsync()
    {
        _logger.LogInformation("Starting manual full backup...");
        
        var result = await _backupService.RunBackupAsync();
        
        if (result.Success)
        {
            Console.WriteLine($"✅ Full backup completed successfully!");
            Console.WriteLine($"   Chats: {result.ChatsProcessed}");
            Console.WriteLine($"   Messages: {result.MessagesProcessed}");
            Console.WriteLine($"   Duration: {result.Duration.TotalSeconds:F1}s");
            return 0;
        }
        else
        {
            Console.WriteLine($"❌ Backup failed: {result.Error}");
            return 1;
        }
    }

    public async Task<int> RunIncrementalBackupAsync()
    {
        _logger.LogInformation("Starting manual incremental backup...");
        
        var result = await _backupService.RunIncrementalBackupAsync();
        
        if (result.Success)
        {
            Console.WriteLine($"✅ Incremental backup completed successfully!");
            Console.WriteLine($"   New messages: {result.MessagesProcessed}");
            Console.WriteLine($"   Duration: {result.Duration.TotalSeconds:F1}s");
            return 0;
        }
        else
        {
            Console.WriteLine($"❌ Backup failed: {result.Error}");
            return 1;
        }
    }
}
