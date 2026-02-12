using System.Text;

namespace PackageUpdateSkill.Services;

public static class SkillWriter
{
    public static async Task<string> WriteSkillFileAsync(
        string repoDir,
        string packageName,
        string fromVersion,
        string toVersion,
        string description,
        string migrationMarkdown)
    {
        var skillName = Slugify(packageName) + "-migration";
        var skillDir = Path.Combine(repoDir, ".copilot", "skills", skillName);
        Directory.CreateDirectory(skillDir);
        Directory.CreateDirectory(Path.Combine(skillDir, "scripts"));
        var path = Path.Combine(skillDir, "SKILL.md");

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {skillName}");
        sb.AppendLine($"description: {EscapeYaml(description)}");
        sb.AppendLine("config:");
        sb.AppendLine($"  package: {EscapeYaml(packageName)}");
        sb.AppendLine($"  from: {EscapeYaml(fromVersion)}");
        sb.AppendLine($"  to: {EscapeYaml(toVersion)}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(migrationMarkdown);

        await File.WriteAllTextAsync(path, sb.ToString());
        return skillDir;
    }

    private static string Slugify(string name) =>
        name.ToLowerInvariant()
            .Replace('.', '-')
            .Replace(' ', '-');

    private static string EscapeYaml(string value)
    {
        // Strip newlines — YAML single-line values must not span lines
        value = value.Replace("\r", "").Replace("\n", " ");

        // Quote if contains any YAML-special characters
        if (value.Contains(':') || value.Contains('#') || value.Contains('"')
            || value.Contains('{') || value.Contains('}') || value.Contains('[')
            || value.Contains(']') || value.Contains('|') || value.Contains('>')
            || value.Contains('&') || value.Contains('*') || value.Contains('!')
            || value.Contains('%') || value.Contains('@') || value.Contains('`')
            || value.StartsWith(' ') || value.EndsWith(' '))
        {
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }

        return value;
    }
}
