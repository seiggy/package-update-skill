using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PackageUpdateSkill.Services;

/// <summary>
/// Downloads and caches the Copilot CLI binary on first run.
/// The SDK expects it at runtimes/{rid}/native/copilot[.exe] relative to the app.
/// When installed as a NuGet tool, the build-time binary may not be present for
/// the current platform — this bootstrapper fetches it from npm on demand.
/// </summary>
public static class CopilotCliBootstrap
{
    private const string CliVersion = "0.0.403";

    public static async Task EnsureCliAvailableAsync()
    {
        var (rid, npmPlatform, binaryName) = GetPlatformInfo();

        // Check the standard SDK location (relative to the executing assembly)
        var appDir = AppContext.BaseDirectory;
        var expectedPath = Path.Combine(appDir, "runtimes", rid, "native", binaryName);

        if (File.Exists(expectedPath))
            return;

        Console.WriteLine($"Copilot CLI not found at expected path — resolving for {npmPlatform}...");

        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "package-update-skill", "copilot-cli", CliVersion, npmPlatform);

        var cachedBinary = Path.Combine(cacheDir, binaryName);

        if (!File.Exists(cachedBinary))
        {
            Console.WriteLine($"  Downloading Copilot CLI {CliVersion} for {npmPlatform}...");
            Directory.CreateDirectory(cacheDir);

            var url = $"https://registry.npmjs.org/@github/copilot-{npmPlatform}/-/copilot-{npmPlatform}-{CliVersion}.tgz";
            var tgzPath = Path.Combine(cacheDir, "copilot.tgz");

            using var http = new HttpClient();
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var fs = File.Create(tgzPath);
            await response.Content.CopyToAsync(fs);
            fs.Close();

            // Extract with tar (available on Windows 10+, Linux, macOS)
            var tarExe = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.SystemDirectory, "tar.exe")
                : "tar";

            var psi = new ProcessStartInfo(tarExe, $"-xzf \"{tgzPath}\" --strip-components=1 -C \"{cacheDir}\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Failed to extract Copilot CLI: {err}");
            }

            // Set executable permission on Unix
            if (!OperatingSystem.IsWindows())
            {
                var chmod = Process.Start("chmod", $"+x \"{cachedBinary}\"");
                if (chmod != null) await chmod.WaitForExitAsync();
            }

            // Clean up tgz
            try { File.Delete(tgzPath); } catch { }
        }
        else
        {
            Console.WriteLine("  Using cached Copilot CLI");
        }

        // Copy cached binary to the expected SDK location
        var targetDir = Path.GetDirectoryName(expectedPath)!;
        Directory.CreateDirectory(targetDir);
        File.Copy(cachedBinary, expectedPath, overwrite: true);

        Console.WriteLine($"  Copilot CLI installed to {expectedPath}");
    }

    private static (string Rid, string NpmPlatform, string Binary) GetPlatformInfo()
    {
        var arch = RuntimeInformation.OSArchitecture;

        if (OperatingSystem.IsWindows())
        {
            return arch switch
            {
                Architecture.X64 => ("win-x64", "win32-x64", "copilot.exe"),
                Architecture.Arm64 => ("win-arm64", "win32-arm64", "copilot.exe"),
                _ => throw new PlatformNotSupportedException($"Unsupported Windows architecture: {arch}")
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return arch switch
            {
                Architecture.X64 => ("linux-x64", "linux-x64", "copilot"),
                Architecture.Arm64 => ("linux-arm64", "linux-arm64", "copilot"),
                _ => throw new PlatformNotSupportedException($"Unsupported Linux architecture: {arch}")
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return arch switch
            {
                Architecture.X64 => ("osx-x64", "darwin-x64", "copilot"),
                Architecture.Arm64 => ("osx-arm64", "darwin-arm64", "copilot"),
                _ => throw new PlatformNotSupportedException($"Unsupported macOS architecture: {arch}")
            };
        }

        throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
    }
}
