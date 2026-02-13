using Spectre.Console;

namespace PackageUpdateSkill;

/// <summary>
/// Immutable CLI options parsed from command-line arguments.
/// </summary>
public class PipelineOptions
{
    public required string PackageName { get; init; }
    public required string FromVersion { get; init; }
    public required string ToVersion { get; init; }
    public string Model { get; init; } = "gpt-5";
    public string RepoDir { get; init; } = Directory.GetCurrentDirectory();
    public bool Paranoid { get; init; }
    public bool Debug { get; init; }

    // Derived paths
    public string WorkDir => Path.Combine(RepoDir, "output", PackageName);
    public string ValidationDir => Path.Combine(WorkDir, "validation");
    public string AnalysesDir => Path.Combine(ValidationDir, "analyses");

    /// <summary>
    /// Parses CLI arguments and validates inputs.
    /// Returns null if validation fails (errors written to stderr/console).
    /// </summary>
    public static PipelineOptions? Parse(string[] args)
    {
        if (args.Length < 3)
        {
            PrintUsage();
            return null;
        }

        var options = new PipelineOptions
        {
            PackageName = args[0],
            FromVersion = args[1],
            ToVersion = args[2],
            Model = GetArg(args, "--model") ?? "gpt-5",
            RepoDir = GetArg(args, "--dir") ?? Directory.GetCurrentDirectory(),
            Paranoid = args.Contains("--paranoid"),
            Debug = args.Contains("--debug"),
        };

        var (pkgValid, pkgError) = Services.ContentSanitizer.ValidatePackageName(options.PackageName);
        if (!pkgValid)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]ERROR:[/] {pkgError}");
            return null;
        }

        var (fromValid, fromError) = Services.ContentSanitizer.ValidateVersion(options.FromVersion);
        var (toValid, toError) = Services.ContentSanitizer.ValidateVersion(options.ToVersion);
        if (!fromValid || !toValid)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]ERROR:[/] {fromError ?? toError}");
            return null;
        }

        if (!Directory.Exists(options.RepoDir))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]ERROR:[/] Repository directory does not exist: {options.RepoDir}");
            return null;
        }

        return options;
    }

    private static void PrintUsage()
    {
        AnsiConsole.MarkupLine("[yellow]Usage:[/] package-update-skill <PackageName> <FromVersion> <ToVersion> [[--model <model>]] [[--dir <repoDir>]] [[--paranoid]] [[--debug]]");
        AnsiConsole.MarkupLine("[dim]Example: dnx PackageUpdateSkill Newtonsoft.Json 12.0.3 13.0.3 --model gpt-5[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Options:[/]");
        AnsiConsole.WriteLine("  --model <model>   LLM model to use (default: gpt-5)");
        AnsiConsole.WriteLine("  --dir <repoDir>   Repository root directory (default: current directory)");
        AnsiConsole.WriteLine("  --paranoid        Enable injection detection — scans external content for");
        AnsiConsole.WriteLine("                    prompt injection attempts, adds defensive prompts, and");
        AnsiConsole.WriteLine("                    generates a security report");
        AnsiConsole.WriteLine("  --debug           Keep intermediate output/validation files after completion");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Environment variables:[/]");
        AnsiConsole.WriteLine("  GITHUB_TOKEN      (optional) GitHub PAT — falls back to Copilot SDK auth");
    }

    private static string? GetArg(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
