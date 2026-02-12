using PackageUpdateSkill.Services;

namespace PackageUpdateSkill.Tests;

/// <summary>
/// Integration tests for pipeline helpers and file I/O patterns
/// used across the 5-phase pipeline.
/// </summary>
public class PipelineIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public PipelineIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pipeline-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── ChunkList helper ─────────────────────────────────────

    [Fact]
    public void ChunkList_SplitsEvenly()
    {
        var source = Enumerable.Range(1, 10).ToList();
        var chunks = ChunkList(source, 5);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(5, chunks[0].Count);
        Assert.Equal(5, chunks[1].Count);
    }

    [Fact]
    public void ChunkList_HandlesRemainder()
    {
        var source = Enumerable.Range(1, 7).ToList();
        var chunks = ChunkList(source, 5);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(5, chunks[0].Count);
        Assert.Equal(2, chunks[1].Count);
    }

    [Fact]
    public void ChunkList_SingleChunkForSmallList()
    {
        var source = new List<string> { "a", "b" };
        var chunks = ChunkList(source, 5);
        Assert.Single(chunks);
        Assert.Equal(2, chunks[0].Count);
    }

    [Fact]
    public void ChunkList_EmptyList()
    {
        var chunks = ChunkList(new List<int>(), 5);
        Assert.Empty(chunks);
    }

    // ── Discovery file parsing ───────────────────────────────

    [Fact]
    public async Task DiscoveryParsing_ExtractsRepoAndVersions()
    {
        var discoveryContent = """
            repo: microsoft/agent-framework
            versions:
            - dotnet-1.0.0-preview.251007.1
            - dotnet-1.0.0-preview.251009.1
            - dotnet-1.0.0-preview.260209.1
            """;

        var path = Path.Combine(_tempDir, "discovery.md");
        await File.WriteAllTextAsync(path, discoveryContent);
        var content = await File.ReadAllTextAsync(path);

        var repoMatch = System.Text.RegularExpressions.Regex.Match(content, @"repo:\s*(\S+)");
        Assert.True(repoMatch.Success);
        Assert.Equal("microsoft/agent-framework", repoMatch.Groups[1].Value);

        var versions = System.Text.RegularExpressions.Regex.Matches(content, @"^-\s*(.+)$",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        Assert.Equal(3, versions.Count);
        Assert.Equal("dotnet-1.0.0-preview.251007.1", versions[0]);
        Assert.Equal("dotnet-1.0.0-preview.260209.1", versions[2]);
    }

    // ── Skill output file structure ──────────────────────────

    [Fact]
    public async Task FullSkillOutput_HasCorrectStructure()
    {
        // Simulate Phase 4 output
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, "Microsoft.Agents.AI.OpenAI",
            "1.0.0-preview.251007.1", "1.0.0-preview.260209.1",
            "Migration guide for MAF", "# Migration\n\nSee [Breaking Changes](breaking-changes.md)");

        // Write breakdown docs
        await File.WriteAllTextAsync(Path.Combine(skillDir, "breaking-changes.md"), "# Breaking Changes");
        await File.WriteAllTextAsync(Path.Combine(skillDir, "api-renames.md"), "# API Renames");

        // Write script
        var scriptDir = Path.Combine(skillDir, "scripts");
        await File.WriteAllTextAsync(Path.Combine(scriptDir, "migrate.csx"),
            "// ⚠ AUTO-GENERATED — Review before running!\nConsole.WriteLine(\"migrating...\");");

        // Verify structure
        Assert.True(File.Exists(Path.Combine(skillDir, "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(skillDir, "breaking-changes.md")));
        Assert.True(File.Exists(Path.Combine(skillDir, "api-renames.md")));
        Assert.True(File.Exists(Path.Combine(scriptDir, "migrate.csx")));

        // Verify skill name
        Assert.EndsWith("microsoft-agents-ai-openai-migration", skillDir);

        // Verify YAML content
        var skillContent = await File.ReadAllTextAsync(Path.Combine(skillDir, "SKILL.md"));
        Assert.StartsWith("---", skillContent);
        Assert.Contains("microsoft-agents-ai-openai-migration", skillContent);
        Assert.Contains("Microsoft.Agents.AI.OpenAI", skillContent);
    }

    // ── File copy logic (simulates post-Phase 4 processing) ──

    [Fact]
    public async Task FileCopy_OnlyMarkdownAndScripts()
    {
        var workDir = Path.Combine(_tempDir, "work");
        var skillDir = Path.Combine(_tempDir, "skill");
        var scriptsDir = Path.Combine(workDir, "scripts");
        var skillScriptsDir = Path.Combine(skillDir, "scripts");
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(skillDir);
        Directory.CreateDirectory(skillScriptsDir);

        // Create work files
        await File.WriteAllTextAsync(Path.Combine(workDir, "breaking-changes.md"), "content");
        await File.WriteAllTextAsync(Path.Combine(workDir, "skill-body.md"), "body"); // should be excluded
        await File.WriteAllTextAsync(Path.Combine(scriptsDir, "migrate.csx"), "script");

        // Simulate the copy logic from Program.cs
        foreach (var mdFile in Directory.GetFiles(workDir, "*.md")
            .Where(f => !Path.GetFileName(f).Equals("skill-body.md", StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(mdFile, Path.Combine(skillDir, Path.GetFileName(mdFile)), overwrite: true);
        }

        foreach (var script in Directory.GetFiles(scriptsDir))
        {
            File.Copy(script, Path.Combine(skillScriptsDir, Path.GetFileName(script)), overwrite: true);
        }

        // Verify
        Assert.True(File.Exists(Path.Combine(skillDir, "breaking-changes.md")));
        Assert.False(File.Exists(Path.Combine(skillDir, "skill-body.md"))); // excluded
        Assert.True(File.Exists(Path.Combine(skillScriptsDir, "migrate.csx")));
    }

    // ── Analysis file tag sanitization ───────────────────────

    [Theory]
    [InlineData("dotnet-1.0.0-preview.251007.1", "dotnet-1.0.0-preview.251007.1.md")]
    [InlineData("v1.0.0", "v1.0.0.md")]
    public void AnalysisFileName_SlashesReplacedWithDash(string tag, string expectedFileName)
    {
        // Simulates the logic: v.Replace('/', '-') + ".md"
        var fileName = tag.Replace('/', '-') + ".md";
        Assert.Equal(expectedFileName, fileName);
    }

    [Fact]
    public void AnalysisFileName_SlashInTag_IsSanitized()
    {
        // A tag like "release/v1.0" should become "release-v1.0.md"
        var tag = "release/v1.0";
        var fileName = tag.Replace('/', '-') + ".md";
        Assert.Equal("release-v1.0.md", fileName);
        Assert.DoesNotContain("/", fileName);
    }

    // ── Helper (mirrors Program.cs) ──────────────────────────

    private static List<List<T>> ChunkList<T>(List<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += chunkSize)
            chunks.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
        return chunks;
    }
}
