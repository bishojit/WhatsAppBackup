using QRCoder;
using WhatsAppBackup.Services;

namespace WhatsAppBackup.Commands;

public interface IConnectCommands
{
    Task<int> CheckConnectionAsync();
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

    public async Task<int> CheckConnectionAsync()
    {
        Console.WriteLine("Checking OpenClaw gateway connection...");
        Console.WriteLine();

        using var initialTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var status = await _openClawClient.GetStatusAsync(initialTimeout.Token);

        if (!status.IsGatewayReachable)
        {
            Console.WriteLine($"❌ Gateway is not reachable: {status.Error}");
            Console.WriteLine();
            Console.WriteLine("Ensure the OpenClaw gateway is running and GatewayUrl in");
            Console.WriteLine("appsettings.json points to the correct address.");
            await PrintCacheInfoAsync();
            return 1;
        }

        Console.WriteLine("✅ Gateway is reachable");

        if (status.IsWhatsAppConnected)
        {
            PrintConnected(status);
            return 0;
        }

        Console.WriteLine("⚠️  WhatsApp is not linked — starting QR scan flow...");
        Console.WriteLine();
        return await WaitForQrScanAsync();
    }

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
            GatewayStatus status;
            try
            {
                using var pollTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                status = await _openClawClient.GetStatusAsync(pollTimeout.Token);
            }
            catch (Exception)
            {
                await DelayAsync(PollIntervalSeconds, cts.Token);
                continue;
            }

            if (status.IsWhatsAppConnected)
            {
                Console.Clear();
                Console.WriteLine("✅ WhatsApp connected successfully!");
                PrintConnected(status);
                return 0;
            }

            if (!string.IsNullOrEmpty(status.QrCode) && status.QrCode != lastQr)
            {
                lastQr = status.QrCode;
                Console.Clear();
                var remaining = QrTimeoutSeconds - (int)(DateTime.UtcNow - startedAt).TotalSeconds;
                Console.WriteLine($"Scan this QR code with WhatsApp  ({remaining}s remaining, Ctrl+C to cancel)");
                Console.WriteLine();
                RenderQrCode(status.QrCode);
            }
            else if (string.IsNullOrEmpty(status.QrCode) && lastQr == null)
            {
                Console.Write(".");
            }

            await DelayAsync(PollIntervalSeconds, cts.Token);
        }

        Console.WriteLine();
        Console.WriteLine("❌ Timed out waiting for QR scan. Run 'connect' again to retry.");
        return 1;
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
        if (info != null)
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

    private static void RenderQrCode(string content)
    {
        try
        {
            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
            var ascii = new AsciiQRCode(qrData);
            Console.WriteLine(ascii.GetGraphic(1, "██", "  "));
        }
        catch
        {
            Console.WriteLine($"[QR render failed — raw data below]");
            Console.WriteLine(content);
        }
    }

    private static async Task DelayAsync(int seconds, CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(seconds), ct); }
        catch (OperationCanceledException) { }
    }
}
