namespace PackageUpdateSkill.Phases;

/// <summary>
/// Phase 5: Cross-reference generated output against source evidence, fix hallucinations.
/// </summary>
public class ReviewPhase(PipelineOptions options, PhaseRunner runner)
{
    public async Task ExecuteAsync(string skillDir, Action<string>? onToolCall = null)
    {
        await runner.RunAsync(
            BuildSystemPrompt(skillDir),
            $"Review the generated skill files at {skillDir} against the evidence in validation/. Fix any issues.",
            onToolCall,
            toolDisplayName: ToolDisplay);
    }

    private static string? ToolDisplay(string toolName) => toolName switch
    {
        "view" or "read_file" or "get_file_contents" => "Reading generated output for audit",
        "write_file" or "create_or_update_file" => "Fixing hallucinations in output",
        "list_directory" => "Scanning skill directory",
        "grep" or "search" => "Cross-referencing against evidence",
        _ => null,
    };

    private string BuildSystemPrompt(string skillDir) => $$"""
        You are a strict technical reviewer auditing migration output for '{{options.PackageName}}'
        ({{options.FromVersion}} → {{options.ToVersion}}) against source evidence.

        The skill directory is: {{skillDir}}
        The layout is:
          SKILL.md              — main skill file with YAML frontmatter
          *.md                  — breakdown docs (breaking-changes.md, api-renames.md, etc.)
          scripts/migrate.csx   — Roslyn migration script
          scripts/*.csx         — any additional helper scripts

        Process:
        1. Read validation/compiled-summary.md (the evidence)
        2. Read SKILL.md at {{skillDir}}
        3. Read all breakdown markdown files in {{skillDir}}
        4. Read scripts/migrate.csx at {{skillDir}}
        5. Spot-check a few files in validation/analyses/

        Check for:
        - Hallucinations: names/APIs/packages in output NOT in evidence → remove them
        - Fabricated abbreviations: made-up shorthand → replace with exact names
        - Missing breaking changes: [BREAKING] items in evidence missing from output → add them
        - Incorrect examples: code that doesn't match release notes → fix
        - Verify SKILL.md references scripts as scripts/migrate.csx (not bare migrate.csx)
        - Verify breakdown docs are cross-referenced from SKILL.md

        If you find issues, rewrite the affected files at {{skillDir}}.
        When rewriting SKILL.md, preserve the YAML frontmatter block at the top (--- delimited).
        List every issue found, even if you also fix it.
        If output is accurate, say so.
        """;
}
