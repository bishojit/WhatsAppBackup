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
    private readonly IOpenClawClient _openClawClient;

    public BackupCommands(
        ILogger<BackupCommands> logger,
        IBackupService backupService,
        IOpenClawClient openClawClient)
    {
        _logger = logger;
        _backupService = backupService;
        _openClawClient = openClawClient;
    }

    public async Task<int> RunFullBackupAsync()
    {
        _logger.LogInformation("Starting manual full backup...");

        // Warn if history sync is not yet complete
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var sync = await _openClawClient.GetSyncStatusAsync(cts.Token);
            if (sync.Connected && !sync.SyncComplete && sync.ChatsCount == 0)
            {
                Console.WriteLine("⚠️  WhatsApp history sync is still in progress (0 chats so far).");
                Console.WriteLine("   Run 'connect' to monitor sync progress before backing up.");
                Console.WriteLine("   If this persists, run 'reset' to re-link your device.");
                Console.WriteLine();
            }
        }
        catch { }
        
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
