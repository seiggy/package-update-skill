using System.Text;
using Spectre.Console;

namespace PackageUpdateSkill.Phases;

/// <summary>
/// Phase 3: Merge and deduplicate per-version analyses into a unified migration guide.
/// </summary>
public class CompilePhase(PipelineOptions options, PhaseRunner runner)
{
    public string CompiledSummary { get; private set; } = "";

    public async Task ExecuteAsync(StringBuilder allAnalyses, Action<string>? onToolCall = null)
    {
        CompiledSummary = await runner.RunAsync(
            SystemPrompt,
            $"Compile these per-version analyses into a unified migration guide for '{options.PackageName}' {options.FromVersion} \u2192 {options.ToVersion}:\n\n{allAnalyses}",
            onToolCall);

        if (string.IsNullOrWhiteSpace(CompiledSummary))
        {
            AnsiConsole.MarkupLine("[yellow]:warning:[/] Compilation empty. Falling back to raw analyses.");
            CompiledSummary = allAnalyses.ToString();
        }

        await File.WriteAllTextAsync(
            Path.Combine(options.ValidationDir, "compiled-summary.md"),
            CompiledSummary);
    }

    private const string SystemPrompt = """
        You are a .NET migration compiler. Compile per-version analyses into a unified migration guide.

        Guidelines:
        - Merge duplicate findings across versions
        - Organize by category: Breaking Changes, Deprecations, Renames, API Signature Changes, New Dependencies
        - Note which version introduced each change
        - Include before/after code examples where the analyses provide them
        - Include PR numbers/links as evidence

        CRITICAL: ONLY include changes from the analyses provided. Do NOT invent names, abbreviations,
        or details. Use exact names from the source. Every item must be traceable to an analysis entry.

        Output the compiled guide directly as your response text (no file writing needed).
        """;
}
