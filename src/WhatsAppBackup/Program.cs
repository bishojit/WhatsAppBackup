using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using WhatsAppBackup.Commands;
using WhatsAppBackup.Configuration;
using WhatsAppBackup.Data;
using WhatsAppBackup.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));

    // Configuration
    builder.Services.Configure<OpenClawSettings>(
        builder.Configuration.GetSection("OpenClaw"));
    builder.Services.Configure<BackupSettings>(
        builder.Configuration.GetSection("Backup"));

    var connectionString = builder.Configuration.GetConnectionString("WhatsAppDb");

    // Database
    builder.Services.AddDbContext<WhatsAppDbContext>(options =>
        options.UseNpgsql(connectionString));

    // HTTP Client for OpenClaw
    builder.Services.AddHttpClient<IOpenClawClient, OpenClawClient>();

    // Services
    builder.Services.AddSingleton<ILocalCacheService, LocalCacheService>();
    builder.Services.AddScoped<IBackupService, BackupService>();
    builder.Services.AddScoped<IBackupCommands, BackupCommands>();
    builder.Services.AddScoped<IConnectCommands, ConnectCommands>();

    // Check for command-line mode
    if (args.Length > 0)
    {
        var host = builder.Build();
        
        using var scope = host.Services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<IBackupCommands>();
        var connect = scope.ServiceProvider.GetRequiredService<IConnectCommands>();

        return args[0].ToLowerInvariant() switch
        {
            "connect" or "--connect" or "-c" => await connect.CheckConnectionAsync(),
            "backup" or "--backup" or "-b" => await commands.RunFullBackupAsync(),
            "incremental" or "--incremental" or "-i" => await commands.RunIncrementalBackupAsync(),
            "migrate" or "--migrate" =>
                await MigrateDatabase(scope.ServiceProvider.GetRequiredService<WhatsAppDbContext>()),
            "help" or "--help" or "-h" => ShowHelp(),
            _ => ShowHelp()
        };
    }

    // Scheduler mode (daemon)
    var backupSettings = builder.Configuration.GetSection("Backup").Get<BackupSettings>()
        ?? new BackupSettings();

    builder.Services.AddQuartz(q =>
    {
        var jobKey = new JobKey("WhatsAppBackupJob");
        q.AddJob<BackupSchedulerJob>(opts => opts.WithIdentity(jobKey));

        q.AddTrigger(opts => opts
            .ForJob(jobKey)
            .WithIdentity("WhatsAppBackupTrigger")
            .WithCronSchedule(backupSettings.ScheduleCron));
    });

    builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

    var app = builder.Build();

    // Ensure database is migrated on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<WhatsAppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrated successfully");
    }

    Log.Information("WhatsApp Backup Service starting...");
    Log.Information("Scheduled backup: {Cron}", backupSettings.ScheduleCron);

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task<int> MigrateDatabase(WhatsAppDbContext db)
{
    Console.WriteLine("Running database migrations...");
    await db.Database.MigrateAsync();
    Console.WriteLine("✅ Database migrated successfully!");
    return 0;
}

static int ShowHelp()
{
    Console.WriteLine("""
        WhatsApp Backup Service
        
        Usage:
          WhatsAppBackup [command]
        
        Commands:
          (none)        Run as a scheduled service (daemon mode)
          connect       Check gateway and WhatsApp connection status
          backup        Run a full backup now
          incremental   Run an incremental backup now
          migrate       Run database migrations only
          help          Show this help message

        Examples:
          WhatsAppBackup                  # Start daemon with scheduled backups
          WhatsAppBackup connect          # Check connection status
          WhatsAppBackup backup           # Run full backup manually
          WhatsAppBackup incremental      # Run incremental backup manually
        
        Configuration:
          Edit appsettings.json to configure:
          - Database connection string
          - OpenClaw gateway URL and token
          - Backup schedule (cron expression)
          - Media storage path
        """);
    return 0;
}
