using Quartz;

namespace WhatsAppBackup.Services;

[DisallowConcurrentExecution]
public class BackupSchedulerJob : IJob
{
    private readonly ILogger<BackupSchedulerJob> _logger;
    private readonly IBackupService _backupService;

    public BackupSchedulerJob(
        ILogger<BackupSchedulerJob> logger,
        IBackupService backupService)
    {
        _logger = logger;
        _backupService = backupService;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Scheduled backup job started at {Time}", DateTime.UtcNow);

        try
        {
            // Run incremental backup for scheduled jobs
            var result = await _backupService.RunIncrementalBackupAsync(context.CancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Scheduled backup completed: {Messages} messages backed up in {Duration:F1}s",
                    result.MessagesProcessed, result.Duration.TotalSeconds);
            }
            else
            {
                _logger.LogError("Scheduled backup failed: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled backup job failed with exception");
            throw new JobExecutionException(ex);
        }
    }
}
