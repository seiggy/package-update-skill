using PackageUpdateSkill.Services;
using Spectre.Console;

namespace PackageUpdateSkill.Phases;

/// <summary>
/// Phase 4: Generate SKILL.md, breakdown docs, and Roslyn migration scripts.
/// </summary>
public class GeneratePhase(PipelineOptions options, PhaseRunner runner)
{
    public string SkillDir { get; private set; } = "";

    public async Task ExecuteAsync(string compiledSummary, Action<string>? onToolCall = null)
    {
        var skillBodyPath = Path.Combine(options.WorkDir, "skill-body.md");
        var skillDescPath = Path.Combine(options.WorkDir, "skill-description.txt");
        var scriptsDir = Path.Combine(options.WorkDir, "scripts");
        Directory.CreateDirectory(scriptsDir);
        var scriptPath = Path.Combine(scriptsDir, "migrate.csx");

        await runner.RunWithRetryAsync(
            BuildSystemPrompt(),
            $"Generate the output files based on this compiled migration summary:\n\n{compiledSummary}",
            () => File.Exists(skillBodyPath) && File.Exists(scriptPath),
            onToolCall,
            toolDisplayName: ToolDisplay);

        // Post-process: wrap skill body with YAML frontmatter
        var skillBody = File.Exists(skillBodyPath) ? await File.ReadAllTextAsync(skillBodyPath) : compiledSummary;
        var skillDesc = File.Exists(skillDescPath)
            ? (await File.ReadAllTextAsync(skillDescPath)).Trim()
            : $"Migration guide for {options.PackageName} from {options.FromVersion} to {options.ToVersion}";

        SkillDir = await SkillWriter.WriteSkillFileAsync(
            options.RepoDir, options.PackageName, options.FromVersion, options.ToVersion,
            skillDesc, skillBody);

        // Copy breakdown markdown files to the skill directory
        foreach (var mdFile in Directory.GetFiles(options.WorkDir, "*.md")
                     .Where(f => !Path.GetFileName(f).Equals("skill-body.md", StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(mdFile, Path.Combine(SkillDir, Path.GetFileName(mdFile)), overwrite: true);
        }

        // Copy scripts to the skill scripts/ directory
        var skillScriptsDir = Path.Combine(SkillDir, "scripts");
        if (Directory.Exists(scriptsDir))
        {
            foreach (var script in Directory.GetFiles(scriptsDir))
                File.Copy(script, Path.Combine(skillScriptsDir, Path.GetFileName(script)), overwrite: true);
        }

        // Clean up intermediate generation files
        if (File.Exists(skillBodyPath)) File.Delete(skillBodyPath);
        if (File.Exists(skillDescPath)) File.Delete(skillDescPath);
    }

    private string BuildSystemPrompt() => $$"""
        You are a .NET migration specialist generating output files for upgrading '{{options.PackageName}}'
        from {{options.FromVersion}} to {{options.ToVersion}}.

        You MUST write the following files:

        1. skill-description.txt — A single line (under 1024 chars) describing when this migration skill applies.

        2. skill-body.md — The main SKILL.md body. This is the overview that ties everything together:
           * Brief summary of the migration scope
           * Links to breakdown docs (e.g., "See [Breaking Changes](breaking-changes.md) for details")
           * Quick-start checklist of migration steps
           * Reference to the migration script: "Run `scripts/migrate.csx` via `dotnet script scripts/migrate.csx`"
           Do NOT include YAML frontmatter. Start directly with markdown headings.

        3. Breakdown markdown files — Split the migration into focused documents by category.
           Write as many as needed. Examples:
           * breaking-changes.md — All breaking changes with before/after code examples
           * api-renames.md — Type, method, and namespace renames
           * dependency-changes.md — Package reference and dependency updates
           * new-features.md — New APIs or patterns to adopt
           * deprecations.md — Deprecated APIs and their replacements
           Each file should be self-contained with full context and code examples.
           Only create files for categories that have actual content.

        4. scripts/migrate.csx — A C# script using Microsoft.CodeAnalysis.CSharp (Roslyn) that:
           * Starts with a comment block: "// AUTO-GENERATED MIGRATION SCRIPT — Review before running!"
           * Defines SyntaxRewriter subclasses for type/method renames, namespace changes
           * Processes all .cs files recursively
           * Prints changes per file
           * Is runnable via `dotnet script scripts/migrate.csx`
           For complex migrations, you may create additional helper scripts in scripts/.

        CRITICAL: ONLY include changes backed by the compiled summary. Use EXACT names from the summary.
        Do NOT invent abbreviations or shorthand for packages or APIs.
        """;

    private static string? ToolDisplay(string toolName) => toolName switch
    {
        "view" or "read_file" or "get_file_contents" => "Reading migration summary",
        "write_file" or "create_or_update_file" => "Writing skill files",
        "create_directory" or "list_directory" => "Preparing output directories",
        _ => null,
    };
}
