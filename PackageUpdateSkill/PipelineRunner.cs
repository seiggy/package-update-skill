using System.Text;
using GitHub.Copilot.SDK;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PackageUpdateSkill.Phases;
using PackageUpdateSkill.Services;
using Spectre.Console;

namespace PackageUpdateSkill;

/// <summary>
/// Orchestrates the 5-phase migration pipeline with Spectre.Console output.
/// </summary>
public class PipelineRunner(PipelineOptions options)
{
    // Security tracking (paranoid mode)
    private readonly List<ContentSanitizer.InjectionWarning> _injectionWarnings = [];
    private readonly List<ContentValidator.ValidationResult> _validationResults = [];

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(options.AnalysesDir);
        PrintBanner();

        // ── Pre-fetch NuGet README ───────────────────────────
        var nugetReadme = await FetchNugetReadmeAsync();

        // ── Start Copilot SDK ────────────────────────────────
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync(":gear: Starting Copilot SDK...", async _ =>
            {
                await CopilotCliBootstrap.EnsureCliAvailableAsync();
            });

        await using var copilot = new CopilotClient(new CopilotClientOptions { LogLevel = "error" });
        await copilot.StartAsync();
        AnsiConsole.MarkupLine("[green]:check_mark:[/] Copilot SDK started");

        var tokenTracker = new TokenTracker();
        var phaseRunner = new PhaseRunner(copilot, options, tokenTracker);
        ContentValidator? validator = options.Paranoid ? new ContentValidator(copilot, options.Model) : null;

        // Paranoid: LLM-validate the NuGet README
        if (options.Paranoid && nugetReadme != "(README unavailable)" && nugetReadme != "(no README available)")
        {
            var llmResult = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("yellow"))
                .StartAsync(":robot: Validating NuGet README...", async _ =>
                    await validator!.ValidateAsync(nugetReadme, "NuGet README"));

            if (!llmResult.IsSafe)
            {
                AnsiConsole.MarkupLine("[yellow]:warning: PARANOID:[/] LLM flagged NuGet README as SUSPICIOUS");
                foreach (var f in llmResult.Findings)
                    AnsiConsole.MarkupLineInterpolated($"  [dim]{f}[/]");
                _validationResults.Add(llmResult);
            }
            else
            {
                AnsiConsole.MarkupLine("[green]:check_mark:[/] NuGet README passed LLM validation");
            }
        }

        // ── Pipeline phases (Live dashboard) ─────────────────
        AnsiConsole.WriteLine();

        const int phaseCount = 5;
        var phaseNames = new[] { "Discovery", "Analyze", "Compile", "Generate", "Review" };
        var phaseStatus = new string[phaseCount];
        var phaseDetail = new string[phaseCount];
        for (int i = 0; i < phaseCount; i++)
        {
            phaseStatus[i] = "[dim]\u25cb pending[/]";
            phaseDetail[i] = "";
        }

        // Shared state set by phases inside the Live block
        string repoRef = "unknown/unknown";
        List<string> versions = [];
        StringBuilder allAnalyses = new();
        int analyzeFileCount = 0;
        string compiledSummary = "";
        string skillDir = "";

        var table = BuildDashboardTable(phaseNames, phaseStatus, phaseDetail, tokenTracker);

        await AnsiConsole.Live(table)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .StartAsync(async ctx =>
            {
                void Refresh()
                {
                    ctx.UpdateTarget(BuildDashboardTable(phaseNames, phaseStatus, phaseDetail, tokenTracker));
                }

                void SetPhase(int idx, string status, string detail = "")
                {
                    phaseStatus[idx] = status;
                    phaseDetail[idx] = detail;
                    Refresh();
                }

                Action<string> ToolCallback(int idx, string prefix) => display =>
                {
                    phaseStatus[idx] = $"[blue]:robot: {Markup.Escape(display)}[/]";
                    Refresh();
                };

                // ── Phase 1: Discovery ───────────────────────
                SetPhase(0, "[blue]:robot: Finding source repository...[/]");
                var discovery = new DiscoveryPhase(options, phaseRunner, nugetReadme);
                await discovery.ExecuteAsync(onToolCall: ToolCallback(0, "Discovery"));
                repoRef = discovery.RepoRef;
                versions = discovery.Versions;
                SetPhase(0, $"[green]:check_mark:[/] [bold]{Markup.Escape(repoRef)}[/]",
                    $"{versions.Count} versions");

                // ── Phase 2: Analyze ─────────────────────────
                SetPhase(1, "[blue]:robot: Starting analysis...[/]");
                var analyze = new AnalyzePhase(options, phaseRunner, repoRef, versions);
                await analyze.ExecuteAsync(
                    onToolCall: ToolCallback(1, "Analyze"),
                    onChunkProgress: (done, total) =>
                    {
                        var pct = total > 0 ? (double)done / total : 0;
                        phaseStatus[1] = $"[blue]{MakeProgressBar(pct)}[/]";
                        phaseDetail[1] = $"Chunk {done}/{total}";
                        Refresh();
                    },
                    paranoid: options.Paranoid,
                    injectionWarnings: _injectionWarnings);
                analyzeFileCount = analyze.FileCount;
                allAnalyses = analyze.AllAnalyses;
                SetPhase(1, $"[green]:check_mark:[/] {analyzeFileCount} analysis files");

                // Paranoid: LLM-validate combined analyses
                if (options.Paranoid)
                {
                    phaseDetail[1] = ":robot: Validating...";
                    Refresh();
                    var analysesText = allAnalyses.ToString();
                    var llmResult = await validator!.ValidateAsync(analysesText, "Combined release note analyses");
                    if (!llmResult.IsSafe)
                    {
                        _validationResults.Add(llmResult);
                        phaseDetail[1] += " [yellow]:warning:[/]";
                    }
                    else
                    {
                        phaseDetail[1] = $"{analyzeFileCount} files [green]:check_mark:[/]";
                    }
                    Refresh();
                }

                // ── Phase 3: Compile ─────────────────────────
                SetPhase(2, "[blue]:robot: Merging and deduplicating...[/]");
                var compile = new CompilePhase(options, phaseRunner);
                await compile.ExecuteAsync(allAnalyses, onToolCall: ToolCallback(2, "Compile"));
                compiledSummary = compile.CompiledSummary;
                SetPhase(2, $"[green]:check_mark:[/] ~{TokenTracker.Format(compiledSummary.Length / 4)} tokens");

                // ── Phase 4: Generate ────────────────────────
                SetPhase(3, "[blue]:robot: Generating SKILL.md and scripts...[/]");
                var generate = new GeneratePhase(options, phaseRunner);
                await generate.ExecuteAsync(compiledSummary, onToolCall: ToolCallback(3, "Generate"));
                skillDir = generate.SkillDir;
                SetPhase(3, $"[green]:check_mark:[/] Skill written");

                // Paranoid: LLM-validate generated scripts
                if (options.Paranoid)
                {
                    var skillScriptsDir = Path.Combine(skillDir, "scripts");
                    if (Directory.Exists(skillScriptsDir))
                    {
                        phaseDetail[3] = ":robot: Reviewing scripts...";
                        Refresh();
                        foreach (var scriptFile in Directory.GetFiles(skillScriptsDir, "*.csx"))
                        {
                            var scriptContent = await File.ReadAllTextAsync(scriptFile);
                            var llmResult = await validator!.ValidateGeneratedScriptAsync(scriptContent, scriptFile);
                            if (!llmResult.IsSafe)
                                _validationResults.Add(llmResult);
                        }
                        var scriptResults = _validationResults.Where(r => r.Source.EndsWith(".csx")).ToList();
                        phaseDetail[3] = scriptResults.Any(r => !r.IsSafe)
                            ? "[yellow]:warning: script issues[/]"
                            : "[green]:check_mark:[/] scripts clean";
                        Refresh();
                    }
                }

                // ── Phase 5: Review ──────────────────────────
                SetPhase(4, "[blue]:robot: Cross-referencing against evidence...[/]");
                var review = new ReviewPhase(options, phaseRunner);
                await review.ExecuteAsync(skillDir, onToolCall: ToolCallback(4, "Review"));
                SetPhase(4, "[green]:check_mark:[/] Review complete");
            });

        // ── Done ─────────────────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold green]Done[/]").LeftJustified());
        AnsiConsole.MarkupLineInterpolated($"  [dim]Tokens:[/] {TokenTracker.Format(tokenTracker.InputTokens)} in, {TokenTracker.Format(tokenTracker.OutputTokens)} out, {TokenTracker.Format(tokenTracker.CacheReadTokens)} cached ({tokenTracker.LlmCalls} LLM calls, {TokenTracker.FormatDuration(tokenTracker.DurationMs)})");

        // Paranoid: write security report
        if (options.Paranoid)
        {
            var reportPath = Path.Combine(options.ValidationDir, "security-report.md");
            await SecurityReportWriter.WriteAsync(
                reportPath, options.PackageName, options.FromVersion, options.ToVersion,
                _injectionWarnings, _validationResults);

            AnsiConsole.MarkupLineInterpolated($"  [dim]:locked: Security report:[/] {reportPath}");

            if (_injectionWarnings.Count > 0 || _validationResults.Any(r => !r.IsSafe))
            {
                AnsiConsole.MarkupLine("[yellow]:warning: Security issues detected in external content or generated code.[/]");
                AnsiConsole.MarkupLine("[yellow]Review the security report and generated files carefully before using.[/]");
            }
        }

        AnsiConsole.MarkupLineInterpolated($"[green]:check_mark:[/] Skill written to [bold]{skillDir}[/]");

        // ── Cleanup ──────────────────────────────────────────
        Cleanup(skillDir);

        return 0;
    }

    private static Table BuildDashboardTable(
        string[] phaseNames, string[] phaseStatus, string[] phaseDetail, TokenTracker tokens)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn("Phase").NoWrap().PadRight(2))
            .AddColumn(new TableColumn("Status"))
            .AddColumn(new TableColumn("Detail").RightAligned().NoWrap());

        for (int i = 0; i < phaseNames.Length; i++)
        {
            var isActive = !phaseStatus[i].Contains("pending") && !phaseStatus[i].Contains(":check_mark:");
            var name = isActive
                ? $"[bold]Phase {i + 1}: {phaseNames[i]}[/]"
                : phaseStatus[i].Contains(":check_mark:")
                    ? $"Phase {i + 1}: {phaseNames[i]}"
                    : $"[dim]Phase {i + 1}: {phaseNames[i]}[/]";

            table.AddRow(
                new Markup(name),
                new Markup(phaseStatus[i]),
                new Markup(phaseDetail[i]));
        }

        // Token metrics rows
        var line1 = $"[cyan]\u2191 {TokenTracker.Format(tokens.InputTokens)}[/] in  [green]\u2193 {TokenTracker.Format(tokens.OutputTokens)}[/] out  [yellow]\u21bb {TokenTracker.Format(tokens.CacheReadTokens)}[/] cached";
        var line2Parts = new List<string>();
        if (tokens.DurationMs > 0) line2Parts.Add($"[blue]{TokenTracker.FormatDuration(tokens.DurationMs)}[/]");
        if (tokens.LlmCalls > 0) line2Parts.Add($"[dim]{tokens.LlmCalls} calls[/]");
        var line2 = line2Parts.Count > 0 ? string.Join("  ", line2Parts) : "";
        table.AddRow(new Markup(""), new Text(""), new Markup(line1));
        if (line2.Length > 0)
            table.AddRow(new Markup(""), new Text(""), new Markup(line2));

        return table;
    }

    private static string MakeProgressBar(double fraction, int width = 20)
    {
        int filled = (int)(fraction * width);
        int empty = width - filled;
        return $"{new string('\u2588', filled)}[dim]{new string('\u2591', empty)}[/] {fraction * 100:0}%";
    }

    private void PrintBanner()
    {
        AnsiConsole.Write(new Rule("[bold blue]Package Update Skill[/]").LeftJustified());
        AnsiConsole.MarkupLineInterpolated($"  [dim]Package:[/] {options.PackageName}");
        AnsiConsole.MarkupLineInterpolated($"  [dim]From:[/]    {options.FromVersion}");
        AnsiConsole.MarkupLineInterpolated($"  [dim]To:[/]      {options.ToVersion}");
        AnsiConsole.MarkupLineInterpolated($"  [dim]Model:[/]   {options.Model}");
        AnsiConsole.MarkupLineInterpolated($"  [dim]Repo:[/]    {options.RepoDir}");
        if (options.Paranoid) AnsiConsole.MarkupLine("  [dim]Mode:[/]    [yellow]:locked: PARANOID — injection scanning enabled[/]");
        if (options.Debug) AnsiConsole.MarkupLine("  [dim]Debug:[/]   [yellow]ON — intermediate files will be retained[/]");
        AnsiConsole.WriteLine();
    }

    private async Task<string> FetchNugetReadmeAsync()
    {
        string nugetReadme = "(README unavailable)";
        try
        {
            nugetReadme = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .StartAsync(":gear: Fetching NuGet package README...", async _ =>
                {
                    await using var nugetClient = await McpClient.CreateAsync(
                        new StdioClientTransport(new StdioClientTransportOptions
                        {
                            Name = "NuGet",
                            Command = "dotnet",
                            Arguments = ["tool", "exec", "NuGet.Mcp.Server", "--source", "https://api.nuget.org/v3/index.json", "--yes"],
                        }),
                        new McpClientOptions { InitializationTimeout = TimeSpan.FromSeconds(120) });

                    var result = await nugetClient.CallToolAsync("get-package-readme", new Dictionary<string, object?>
                    {
                        ["packageName"] = options.PackageName,
                    });
                    var readme = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "(no README available)";
                    return ContentSanitizer.SanitizeForPrompt(readme);
                });

            AnsiConsole.MarkupLine($"[green]:check_mark:[/] NuGet README fetched ({nugetReadme.Length} chars)");

            if (options.Paranoid)
            {
                var warnings = ContentSanitizer.ScanForInjectionAttempts(nugetReadme, "NuGet README");
                if (warnings.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[yellow]:warning: PARANOID:[/] {warnings.Count} regex hit(s) in NuGet README");
                    foreach (var w in warnings)
                        AnsiConsole.MarkupLineInterpolated($"  [dim]- [[{w.Pattern}]] at pos {w.Position}: {w.Context}[/]");
                    _injectionWarnings.AddRange(warnings);
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]:warning:[/] Could not fetch NuGet README: {ex.Message}");
        }

        return nugetReadme;
    }

    private void Cleanup(string skillDir)
    {
        if (!options.Debug)
        {
            try
            {
                if (Directory.Exists(options.WorkDir))
                {
                    Directory.Delete(options.WorkDir, recursive: true);

                    // Remove the output/<package> parent if empty
                    var outputDir = Path.Combine(options.RepoDir, "output");
                    if (Directory.Exists(outputDir) && !Directory.EnumerateFileSystemEntries(outputDir).Any())
                        Directory.Delete(outputDir);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]:warning:[/] Could not clean up output directory: {ex.Message}");
            }
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"[dim]Debug mode: intermediate files retained at {options.WorkDir}[/]");
        }
    }
}
