using PackageUpdateSkill.Services;

namespace PackageUpdateSkill.Tests;

public class ContentSanitizerValidationTests
{
    // ── Package Name Validation ──────────────────────────────

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("Microsoft.Agents.AI.OpenAI")]
    [InlineData("System.Text.Json")]
    [InlineData("xunit")]
    [InlineData("My-Package_v2")]
    public void ValidatePackageName_AcceptsValidNames(string name)
    {
        var (isValid, error) = ContentSanitizer.ValidatePackageName(name);
        Assert.True(isValid, error);
    }

    [Theory]
    [InlineData("", "cannot be empty")]
    [InlineData("   ", "cannot be empty")]
    [InlineData("../../../etc/passwd", "path traversal")]
    [InlineData("..\\windows\\system32", "path traversal")]
    [InlineData("pkg/../../escape", "path traversal")]
    [InlineData("pkg\\..\\escape", "path traversal")]
    [InlineData("valid..name", "path traversal")] // double-dot
    public void ValidatePackageName_RejectsPathTraversal(string name, string expectedErrorFragment)
    {
        var (isValid, error) = ContentSanitizer.ValidatePackageName(name);
        Assert.False(isValid);
        Assert.Contains(expectedErrorFragment, error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePackageName_RejectsOverlongName()
    {
        var name = new string('a', 257);
        var (isValid, _) = ContentSanitizer.ValidatePackageName(name);
        Assert.False(isValid);
    }

    [Theory]
    [InlineData("pkg with spaces")]
    [InlineData("pkg;drop table")]
    [InlineData("pkg<script>")]
    [InlineData(".starts-with-dot")]
    public void ValidatePackageName_RejectsInvalidCharacters(string name)
    {
        var (isValid, _) = ContentSanitizer.ValidatePackageName(name);
        Assert.False(isValid);
    }

    // ── Version Validation ───────────────────────────────────

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("13.0.3")]
    [InlineData("1.0.0-preview.251007.1")]
    [InlineData("1.0.0-beta.1")]
    [InlineData("2.7.0-beta.2")]
    [InlineData("10.0.0+build123")]
    public void ValidateVersion_AcceptsValidVersions(string version)
    {
        var (isValid, error) = ContentSanitizer.ValidateVersion(version);
        Assert.True(isValid, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("1.0")]
    [InlineData("abc.def.ghi")]
    public void ValidateVersion_RejectsInvalidVersions(string version)
    {
        var (isValid, _) = ContentSanitizer.ValidateVersion(version);
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateVersion_RejectsOverlongVersion()
    {
        var version = "1.0.0-" + new string('a', 130);
        var (isValid, _) = ContentSanitizer.ValidateVersion(version);
        Assert.False(isValid);
    }
}
