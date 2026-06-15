using QRCoder;
using WhatsAppBackup.Services;

namespace WhatsAppBackup.Commands;

public interface IConnectCommands
{
    Task<int> CheckConnectionAsync(bool reset = false);
}

public class ConnectCommands : IConnectCommands
{
    private readonly ILogger<ConnectCommands> _logger;
    private readonly IOpenClawClient _openClawClient;
    private readonly ILocalCacheService _cache;

    private const int QrTimeoutSeconds = 120;
    private const int PollIntervalSeconds = 15;

    public ConnectCommands(
        ILogger<ConnectCommands> logger,
        IOpenClawClient openClawClient,
        ILocalCacheService cache)
    {
        _logger = logger;
        _openClawClient = openClawClient;
        _cache = cache;
    }

    public async Task<int> CheckConnectionAsync(bool reset = false)
    {
        if (reset) ResetSession();

        Console.WriteLine("Checking WhatsApp gateway...");
        Console.WriteLine();

        var status = await GetStatusAsync();

        if (!status.IsGatewayReachable)
            status = await TryStartGatewayAsync() ?? status;

        if (!status.IsGatewayReachable)
        {
            Console.WriteLine($"❌ Gateway is not reachable: {status.Error}");
            Console.WriteLine();
            Console.WriteLine("Ensure the gateway is running and GatewayUrl in");
            Console.WriteLine("appsettings.json points to the correct address.");
            await PrintCacheInfoAsync();
            return 1;
        }

        Console.WriteLine("✅ Gateway is reachable");

        if (status.IsWhatsAppConnected)
        {
            PrintConnected(status);
            Console.WriteLine();
            await WaitForHistorySyncAsync();
            return 0;
        }

        Console.WriteLine("⚠️  WhatsApp is not linked — starting QR scan flow...");
        Console.WriteLine();
        return await WaitForQrScanAsync();
    }

    // ── Gateway auto-start ────────────────────────────────────────────────────

    private async Task<GatewayStatus?> TryStartGatewayAsync()
    {
        var gatewayDir = GatewayProcess.FindGatewayDirectory();
        if (gatewayDir is null)
        {
            _logger.LogDebug("No bundled gateway found");
            return null;
        }

        if (!GatewayProcess.IsNodeAvailable())
        {
            Console.WriteLine("ℹ️  Node.js is not installed — cannot auto-start the gateway.");
            Console.WriteLine("   Install it from https://nodejs.org/ and run 'connect' again.");
            return null;
        }

        if (!await GatewayProcess.EnsureDependenciesAsync(gatewayDir))
        {
            Console.WriteLine("❌ npm install failed. Check output above for errors.");
            return null;
        }

        var sessionDir = Path.GetFullPath(Path.Combine(_cache.CacheDirectory, "wa-session"));
        Console.WriteLine($"Starting bundled gateway  (session → {sessionDir})...");

        var proc = GatewayProcess.Start(gatewayDir, sessionDir);
        if (proc is null)
        {
            Console.WriteLine("❌ Failed to launch gateway process.");
            return null;
        }

        Console.Write("Waiting for gateway to start");
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(1000);
            Console.Write(".");
            var s = await GetStatusAsync(timeoutSeconds: 2);
            if (s.IsGatewayReachable)
            {
                Console.WriteLine(" ready!");
                return s;
            }
        }

        Console.WriteLine();
        Console.WriteLine("❌ Gateway did not respond within 20 s.");
        return null;
    }

    // ── QR polling loop ───────────────────────────────────────────────────────

    private async Task<int> WaitForQrScanAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(QrTimeoutSeconds));
        string? lastQr = null;
        var startedAt = DateTime.UtcNow;

        Console.WriteLine("Open WhatsApp on your phone:");
        Console.WriteLine("  Settings → Linked Devices → Link a Device");
        Console.WriteLine();

        while (!cts.IsCancellationRequested)
        {
            var status = await GetStatusAsync(timeoutSeconds: 5);

            if (status.IsWhatsAppConnected)
            {
                ClearConsole();
                Console.WriteLine("✅ WhatsApp connected successfully!");
                PrintConnected(status);
                Console.WriteLine();
                await WaitForHistorySyncAsync();
                return 0;
            }

            if (!string.IsNullOrEmpty(status.QrCode) && status.QrCode != lastQr)
            {
                lastQr = status.QrCode;
                var remaining = QrTimeoutSeconds - (int)(DateTime.UtcNow - startedAt).TotalSeconds;
                ClearConsole();
                Console.WriteLine($"Scan this QR code with WhatsApp  ({remaining}s remaining — Ctrl+C to cancel)");
                Console.WriteLine();
                RenderQrCode(status.QrCode);
            }
            else if (string.IsNullOrEmpty(status.QrCode))
            {
                var gatewayState = status.State ?? "waiting";
                Console.WriteLine($"   Gateway state: {gatewayState} — waiting for QR...");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), cts.Token); }
            catch (OperationCanceledException) { break; }
        }

        Console.WriteLine();
        Console.WriteLine("❌ Timed out. Run 'connect' again to retry.");
        return 1;
    }

    // ── Session reset ─────────────────────────────────────────────────────────

    private void ResetSession()
    {
        var sessionDir = Path.GetFullPath(Path.Combine(_cache.CacheDirectory, "wa-session"));
        if (Directory.Exists(sessionDir))
        {
            Directory.Delete(sessionDir, recursive: true);
            Console.WriteLine($"✅ Session cleared: {sessionDir}");
        }
        else
        {
            Console.WriteLine("No session found — will create a fresh one.");
        }
        Console.WriteLine();
    }

    // ── History sync wait ─────────────────────────────────────────────────────

    private async Task WaitForHistorySyncAsync()
    {
        // Quick check — already complete?
        using var quickCheck = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        SyncStatus initial;
        try { initial = await _openClawClient.GetSyncStatusAsync(quickCheck.Token); }
        catch { initial = new SyncStatus(); }

        if (initial.SyncComplete && initial.ChatsCount > 0)
        {
            Console.WriteLine($"✅ History ready — {initial.ChatsCount} chats, {initial.MessagesCount} messages");
            Console.WriteLine("   Run 'backup' to store everything to PostgreSQL.");
            return;
        }

        Console.WriteLine("Waiting for WhatsApp history sync...");
        Console.WriteLine("(This may take several minutes for large accounts — Ctrl+C to exit and check later)");
        Console.WriteLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int lastChats = -1, lastMessages = -1;
        var noProgressSince = sw.Elapsed;

        while (sw.Elapsed.TotalMinutes < 15)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            SyncStatus sync;
            try { sync = await _openClawClient.GetSyncStatusAsync(cts.Token); }
            catch { sync = new SyncStatus(); }

            bool changed = sync.ChatsCount != lastChats || sync.MessagesCount != lastMessages;
            if (changed)
            {
                noProgressSince = sw.Elapsed;
                lastChats = sync.ChatsCount;
                lastMessages = sync.MessagesCount;
                Console.WriteLine($"   Syncing  Chats: {sync.ChatsCount,5}   Messages: {sync.MessagesCount,7}   ({sw.Elapsed:mm\\:ss})");
            }

            if (sync.SyncComplete)
            {
                Console.WriteLine();
                Console.WriteLine($"✅ Sync complete — {sync.ChatsCount} chats, {sync.MessagesCount} messages");
                Console.WriteLine("   Run 'backup' to store everything to PostgreSQL.");
                return;
            }

            // If no chats after 3 minutes with no progress, the session may be stale
            var stuck = sw.Elapsed - noProgressSince;
            if (sync.ChatsCount == 0 && stuck.TotalMinutes >= 3)
            {
                Console.WriteLine();
                Console.WriteLine("⚠️  No chats received after 3 minutes.");
                Console.WriteLine("   WhatsApp may not resend history for this session.");
                Console.WriteLine("   Run 'reset' to re-link and force a fresh history sync:");
                Console.WriteLine("   dotnet run --project src/WhatsAppBackup -- reset");
                return;
            }

            await Task.Delay(3000);
        }

        Console.WriteLine();
        Console.WriteLine($"⚠️  Sync timeout — {lastChats} chats so far. Run 'backup' when ready.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<GatewayStatus> GetStatusAsync(int timeoutSeconds = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { return await _openClawClient.GetStatusAsync(cts.Token); }
        catch { return new GatewayStatus { IsGatewayReachable = false, Error = "Timeout" }; }
    }

    private static void PrintConnected(GatewayStatus status)
    {
        if (!string.IsNullOrEmpty(status.Phone))
            Console.WriteLine($"   Phone   : {status.Phone}");
        if (!string.IsNullOrEmpty(status.DisplayName))
            Console.WriteLine($"   Name    : {status.DisplayName}");
        if (!string.IsNullOrEmpty(status.State))
            Console.WriteLine($"   State   : {status.State}");
    }

    private async Task PrintCacheInfoAsync()
    {
        var info = await _cache.GetInfoAsync();
        Console.WriteLine();
        if (info is not null)
        {
            Console.WriteLine($"📁 Local cache: {Path.GetFullPath(_cache.CacheDirectory)}");
            Console.WriteLine($"   Last saved : {info.SavedAt:yyyy-MM-dd HH:mm} UTC");
            Console.WriteLine($"   Chats      : {info.ChatCount}");
            Console.WriteLine($"   Messages   : {info.MessageCount}");
        }
        else
        {
            Console.WriteLine($"No local cache found at {_cache.CacheDirectory}.");
            Console.WriteLine("Run 'backup' after connecting to populate the local cache.");
        }
    }

    private static void ClearConsole()
    {
        try { Console.Clear(); }
        catch { Console.WriteLine(new string('-', 60)); }
    }

    private static void RenderQrCode(string content)
    {
        try
        {
            var gen = new QRCodeGenerator();
            var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
            Console.WriteLine(new AsciiQRCode(data).GetGraphic(1, "██", "  "));
        }
        catch
        {
            Console.WriteLine("[QR render failed — raw data:]");
            Console.WriteLine(content);
        }
    }
}
