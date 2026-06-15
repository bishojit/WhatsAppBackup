namespace WhatsAppBackup.Configuration;

public class OpenClawSettings
{
    public string GatewayUrl { get; set; } = "http://localhost:18789";
    public string? GatewayToken { get; set; }
}

public class BackupSettings
{
    /// <summary>
    /// Cron expression for scheduled backups (default: 3 AM daily)
    /// </summary>
    public string ScheduleCron { get; set; } = "0 0 3 * * ?";
    
    /// <summary>
    /// Path to store downloaded media files
    /// </summary>
    public string MediaStoragePath { get; set; } = "./media";
    
    /// <summary>
    /// Whether to download and store media files
    /// </summary>
    public bool EnableMediaDownload { get; set; } = true;
}
