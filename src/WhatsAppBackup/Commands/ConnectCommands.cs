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

    public ConnectCommands(
        ILogger<ConnectCommands> logger,
        IOpenClawClient openClawClient)
    {
        _logger = logger;
        _openClawClient = openClawClient;
    }

    public async Task<int> CheckConnectionAsync()
    {
        Console.WriteLine("Checking OpenClaw gateway connection...");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var status = await _openClawClient.GetStatusAsync(cts.Token);

        if (!status.IsGatewayReachable)
        {
            Console.WriteLine($"❌ Gateway is not reachable: {status.Error}");
            Console.WriteLine();
            Console.WriteLine("Ensure the OpenClaw gateway is running and the GatewayUrl in");
            Console.WriteLine("appsettings.json points to the correct address.");
            return 1;
        }

        Console.WriteLine("✅ Gateway is reachable");

        if (!status.IsWhatsAppConnected)
        {
            Console.WriteLine("⚠️  WhatsApp is not connected");

            if (!string.IsNullOrEmpty(status.QrCode))
            {
                Console.WriteLine();
                Console.WriteLine("Scan the QR code below with WhatsApp on your phone:");
                Console.WriteLine("(WhatsApp → Linked Devices → Link a Device)");
                Console.WriteLine();
                Console.WriteLine(status.QrCode);
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Open the OpenClaw gateway UI in a browser and scan the QR code");
                Console.WriteLine("to link your WhatsApp account.");
                if (!string.IsNullOrEmpty(status.Error))
                    Console.WriteLine($"   Details: {status.Error}");
            }

            return 1;
        }

        Console.WriteLine("✅ WhatsApp is connected");

        if (!string.IsNullOrEmpty(status.Phone))
            Console.WriteLine($"   Phone   : {status.Phone}");
        if (!string.IsNullOrEmpty(status.DisplayName))
            Console.WriteLine($"   Name    : {status.DisplayName}");
        if (!string.IsNullOrEmpty(status.State))
            Console.WriteLine($"   State   : {status.State}");

        return 0;
    }
}
