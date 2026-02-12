using System.Text.RegularExpressions;

namespace PackageUpdateSkill.Services;

/// <summary>
/// Detects and neutralizes prompt injection attempts in external content
/// (NuGet READMEs, GitHub release notes) before feeding to LLM prompts.
/// </summary>
public static partial class ContentSanitizer
{
    /// <summary>Maximum characters allowed from a single external content source.</summary>
    public const int DefaultMaxContentLength = 50_000;

    // Patterns that indicate prompt injection attempts — trying to override system instructions
    private static readonly string[] InjectionMarkers =
    [
        "ignore previous instructions",
        "ignore all previous",
        "ignore your instructions",
        "disregard previous",
        "disregard your instructions",
        "forget your instructions",
        "forget previous",
        "new instructions:",
        "system prompt:",
        "you are now",
        "act as if",
        "pretend you are",
        "override your",
        "from now on you",
        "ignore the above",
        "do not follow",
        "<|im_start|>",
        "<|im_end|>",
        "<|system|>",
        "<|endoftext|>",
        "```system",
        "[SYSTEM]",
        "[INST]",
        "<<SYS>>",
        "</s>",
    ];

    /// <summary>
    /// Validates a NuGet package name — must be safe for file paths and prompts.
    /// </summary>
    public static (bool IsValid, string? Error) ValidatePackageName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Package name cannot be empty.");

        if (name.Contains("..") || name.Contains('/') || name.Contains('\\'))
            return (false, $"Package name contains path traversal characters: '{name}'");

        if (name.Length > 256)
            return (false, $"Package name exceeds 256 characters.");

        // NuGet package names: letters, digits, dots, hyphens, underscores
        if (!PackageNameRegex().IsMatch(name))
            return (false, $"Package name contains invalid characters: '{name}'. Expected alphanumeric, dots, hyphens, underscores.");

        return (true, null);
    }

    /// <summary>
    /// Validates a version string — basic semver-ish format.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return (false, "Version cannot be empty.");

        if (version.Length > 128)
            return (false, "Version string exceeds 128 characters.");

        // Allow semver with prerelease: 1.0.0, 1.0.0-preview.1, 1.0.0-beta.1+build
        if (!VersionRegex().IsMatch(version))
            return (false, $"Version has unexpected format: '{version}'. Expected semver-like (e.g., 1.0.0, 1.0.0-preview.1).");

        return (true, null);
    }

    /// <summary>
    /// Sanitizes external content for safe inclusion in LLM prompts.
    /// Truncates to max length and optionally scans for injection attempts.
    /// </summary>
    public static string SanitizeForPrompt(string content, int maxLength = DefaultMaxContentLength)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Truncate oversized content
        if (content.Length > maxLength)
            content = content[..maxLength] + $"\n\n[TRUNCATED — original was {content.Length} chars]";

        return content;
    }

    /// <summary>
    /// Scans content for prompt injection patterns. Returns a list of warnings.
    /// Does NOT modify content — caller decides what to do.
    /// </summary>
    public static List<InjectionWarning> ScanForInjectionAttempts(string content, string sourceName)
    {
        var warnings = new List<InjectionWarning>();
        if (string.IsNullOrEmpty(content))
            return warnings;

        var lower = content.ToLowerInvariant();
        foreach (var marker in InjectionMarkers)
        {
            var idx = lower.IndexOf(marker.ToLowerInvariant(), StringComparison.Ordinal);
            if (idx >= 0)
            {
                // Extract context around the match
                var start = Math.Max(0, idx - 40);
                var end = Math.Min(content.Length, idx + marker.Length + 40);
                var context = content[start..end].Replace('\n', ' ').Replace('\r', ' ');

                warnings.Add(new InjectionWarning
                {
                    Source = sourceName,
                    Pattern = marker,
                    Position = idx,
                    Context = $"...{context}...",
                });
            }
        }

        return warnings;
    }

    /// <summary>
    /// Wraps external content in clear delimiters so the LLM knows it's untrusted data.
    /// </summary>
    public static string WrapUntrustedContent(string content, string label)
    {
        return $"""
            <untrusted-content source="{label}">
            {content}
            </untrusted-content>
            """;
    }

    public record InjectionWarning
    {
        public required string Source { get; init; }
        public required string Pattern { get; init; }
        public required int Position { get; init; }
        public required string Context { get; init; }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._\-]*$")]
    private static partial Regex PackageNameRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+([.\-+][a-zA-Z0-9.\-+]*)?$")]
    private static partial Regex VersionRegex();
}
