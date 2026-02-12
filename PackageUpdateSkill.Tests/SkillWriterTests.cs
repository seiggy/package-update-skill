using PackageUpdateSkill.Services;

namespace PackageUpdateSkill.Tests;

public class SkillWriterTests : IDisposable
{
    private readonly string _tempDir;

    public SkillWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "skill-writer-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task WriteSkillFileAsync_CreatesCorrectDirectoryStructure()
    {
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, "Newtonsoft.Json", "12.0.0", "13.0.3",
            "Migration guide", "# Content");

        Assert.True(Directory.Exists(skillDir));
        Assert.True(File.Exists(Path.Combine(skillDir, "SKILL.md")));
        Assert.True(Directory.Exists(Path.Combine(skillDir, "scripts")));

        // Verify path is under .copilot/skills/
        Assert.Contains(".copilot", skillDir);
        Assert.Contains("skills", skillDir);
        Assert.Contains("newtonsoft-json-migration", skillDir);
    }

    [Fact]
    public async Task WriteSkillFileAsync_ProducesValidYamlFrontmatter()
    {
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, "My.Package", "1.0.0", "2.0.0",
            "A simple description", "# Migration Guide\n\nContent here.");

        var content = await File.ReadAllTextAsync(Path.Combine(skillDir, "SKILL.md"));
        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Assert.Equal("---", lines[0]);
        Assert.Equal("name: my-package-migration", lines[1]);
        Assert.Equal("description: A simple description", lines[2]);
        Assert.Equal("config:", lines[3]);
        Assert.Contains("package:", lines[4]);
        Assert.Contains("from:", lines[5]);
        Assert.Contains("to:", lines[6]);
        Assert.Equal("---", lines[7]);
    }

    [Fact]
    public async Task WriteSkillFileAsync_SingleYamlFrontmatter()
    {
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, "Test.Pkg", "1.0.0", "2.0.0",
            "desc", "# Body");

        var content = await File.ReadAllTextAsync(Path.Combine(skillDir, "SKILL.md"));
        var separatorCount = content.Split('\n')
            .Count(l => l.TrimEnd('\r') == "---");

        Assert.Equal(2, separatorCount); // opening + closing, not duplicated
    }

    // ── YAML Escaping Tests ──────────────────────────────────

    [Fact]
    public async Task WriteSkillFileAsync_EscapesColonsInDescription()
    {
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, "Test.Pkg", "1.0.0", "2.0.0",
            "Migration: breaking changes for MyPkg: v2", "# Body");

        var content = await File.ReadAllTextAsync(Path.Combine(skillDir, "SKILL.md"));
        Assert.Contains("\"Migration: breaking changes for MyPkg: v2\"", content);
    }

    [Fact]
    public async Task WriteSkillFileAsync_EscapesQuotesInDescription()
    {
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, "Test.Pkg", "1.0.0", "2.0.0",
            "Use \"new\" API", "# Body");

        var content = await File.ReadAllTextAsync(Path.Combine(skillDir, "SKILL.md"));
        // Should escape the inner quotes
        Assert.Contains("\\\"new\\\"", content);
    }

    [Fact]
    public async Task WriteSkillFileAsync_StripsNewlinesFromDescription()
    {
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, "Test.Pkg", "1.0.0", "2.0.0",
            "Line one\nLine two\r\nLine three", "# Body");

        var content = await File.ReadAllTextAsync(Path.Combine(skillDir, "SKILL.md"));
        // Description should be on a single line
        var descLine = content.Split('\n').First(l => l.Contains("description:"));
        Assert.DoesNotContain("\n", descLine.TrimEnd('\r'));
    }

    [Fact]
    public async Task WriteSkillFileAsync_EscapesYamlSpecialCharacters()
    {
        // YAML-special chars: { } [ ] | > & * ! % @ `
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, "Test.Pkg", "1.0.0", "2.0.0",
            "Uses {braces} and [brackets]", "# Body");

        var content = await File.ReadAllTextAsync(Path.Combine(skillDir, "SKILL.md"));
        Assert.Contains("\"Uses {braces} and [brackets]\"", content);
    }

    // ── Slugification Tests ──────────────────────────────────

    [Theory]
    [InlineData("Newtonsoft.Json", "newtonsoft-json-migration")]
    [InlineData("Microsoft.Agents.AI.OpenAI", "microsoft-agents-ai-openai-migration")]
    [InlineData("My Package", "my-package-migration")]
    public async Task WriteSkillFileAsync_SlugifiesSkillName(string packageName, string expectedSkillName)
    {
        var skillDir = await SkillWriter.WriteSkillFileAsync(
            _tempDir, packageName, "1.0.0", "2.0.0", "desc", "# Body");

        Assert.EndsWith(expectedSkillName, skillDir);
        var content = await File.ReadAllTextAsync(Path.Combine(skillDir, "SKILL.md"));
        Assert.Contains($"name: {expectedSkillName}", content);
    }
}
