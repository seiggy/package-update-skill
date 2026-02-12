using PackageUpdateSkill.Services;

namespace PackageUpdateSkill.Tests;

public class ContentSanitizerSanitizationTests
{
    [Fact]
    public void SanitizeForPrompt_TruncatesOversizedContent()
    {
        var content = new string('x', 60_000);
        var result = ContentSanitizer.SanitizeForPrompt(content, maxLength: 50_000);

        Assert.True(result.Length < content.Length);
        Assert.Contains("[TRUNCATED", result);
        Assert.Contains("60000", result); // original length noted
    }

    [Fact]
    public void SanitizeForPrompt_PreservesShortContent()
    {
        var content = "This is a normal README with no issues.";
        var result = ContentSanitizer.SanitizeForPrompt(content);
        Assert.Equal(content, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SanitizeForPrompt_HandlesNullAndEmpty(string? content)
    {
        var result = ContentSanitizer.SanitizeForPrompt(content!);
        Assert.Equal(content, result);
    }

    [Fact]
    public void SanitizeForPrompt_RespectsCustomMaxLength()
    {
        var content = new string('a', 200);
        var result = ContentSanitizer.SanitizeForPrompt(content, maxLength: 100);
        Assert.StartsWith(new string('a', 100), result);
        Assert.Contains("[TRUNCATED", result);
    }

    [Fact]
    public void WrapUntrustedContent_WrapsWithDelimiters()
    {
        var content = "Some README content";
        var result = ContentSanitizer.WrapUntrustedContent(content, "NuGet README");

        Assert.Contains("<untrusted-content", result);
        Assert.Contains("NuGet README", result);
        Assert.Contains("Some README content", result);
        Assert.Contains("</untrusted-content>", result);
    }
}
