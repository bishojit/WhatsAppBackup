using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WhatsAppBackup.Services;

public static class GatewayProcess
{
    private static readonly string NodeExe = "node";
    private static readonly string NpmExe =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "npm.cmd" : "npm";

    /// <summary>
    /// Walks up from the application base and CWD looking for a gateway/package.json.
    /// </summary>
    public static string? FindGatewayDirectory()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        // Walk up from AppBase (handles dotnet run from project subfolder)
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
            roots.Add(dir);
        }

        return roots
            .Select(r => Path.Combine(r, "gateway"))
            .FirstOrDefault(d => Directory.Exists(d) && File.Exists(Path.Combine(d, "package.json")));
    }

    public static bool IsNodeAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(NodeExe, "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            return p is not null && p.WaitForExit(5000) && p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static async Task<bool> EnsureDependenciesAsync(string gatewayDir)
    {
        if (Directory.Exists(Path.Combine(gatewayDir, "node_modules")))
            return true;

        Console.WriteLine("Installing gateway dependencies (first run — may take a minute)...");

        using var p = Process.Start(new ProcessStartInfo(NpmExe, "install")
        {
            WorkingDirectory = gatewayDir,
            UseShellExecute = false,
            CreateNoWindow = false
        });

        if (p is null) return false;
        await p.WaitForExitAsync();
        return p.ExitCode == 0;
    }

    /// <summary>
    /// Starts gateway/index.js as a detached background process.
    /// The process outlives the caller so subsequent backup runs can reuse it.
    /// </summary>
    public static Process? Start(string gatewayDir, string sessionDir)
    {
        var psi = new ProcessStartInfo(NodeExe, "index.js")
        {
            WorkingDirectory = gatewayDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        psi.Environment["SESSION_DIR"] = sessionDir;
        psi.Environment["PORT"] = "18789";

        return Process.Start(psi);
    }
}
