using System.Text;

namespace PackageUpdateSkill.Services;

/// <summary>
/// Generates the paranoid mode security report from accumulated scan results.
/// </summary>
public static class SecurityReportWriter
{
    public static async Task WriteAsync(
        string reportPath,
        string packageName,
        string fromVersion,
        string toVersion,
        List<ContentSanitizer.InjectionWarning> injectionWarnings,
        List<ContentValidator.ValidationResult> validationResults)
    {
        var report = new StringBuilder();
        report.AppendLine("# Security Report \u2014 Paranoid Mode");
        report.AppendLine();
        report.AppendLine($"Package: {packageName}");
        report.AppendLine($"Versions: {fromVersion} \u2192 {toVersion}");
        report.AppendLine($"Scanned: {DateTime.UtcNow:u}");
        report.AppendLine();

        if (injectionWarnings.Count == 0 && validationResults.All(r => r.IsSafe))
        {
            report.AppendLine("## \u2705 No injection attempts detected");
            report.AppendLine();
            report.AppendLine("All external content was scanned using both regex patterns and LLM semantic analysis. No suspicious patterns were found.");
        }
        else
        {
            var totalIssues = injectionWarnings.Count + validationResults.Count(r => !r.IsSafe);
            report.AppendLine($"## \u26a0 {totalIssues} issue(s) detected");
            report.AppendLine();

            if (injectionWarnings.Count > 0)
            {
                report.AppendLine("### Regex Pattern Matches");
                report.AppendLine();
                report.AppendLine("| Source | Pattern | Position | Context |");
                report.AppendLine("|--------|---------|----------|---------|");
                foreach (var w in injectionWarnings)
                    report.AppendLine($"| {w.Source} | `{w.Pattern}` | {w.Position} | {w.Context.Replace("|", "\\|")} |");
                report.AppendLine();
            }

            var suspiciousResults = validationResults.Where(r => !r.IsSafe).ToList();
            if (suspiciousResults.Count > 0)
            {
                report.AppendLine("### LLM Semantic Analysis");
                report.AppendLine();
                foreach (var r in suspiciousResults)
                {
                    report.AppendLine($"**{r.Source}** \u2014 SUSPICIOUS");
                    foreach (var f in r.Findings)
                        report.AppendLine($"  {f}");
                    report.AppendLine();
                }
            }

            report.AppendLine("Review the generated SKILL.md and migrate.csx carefully before using.");
        }

        report.AppendLine();
        report.AppendLine("## Recommendations");
        report.AppendLine();
        report.AppendLine("- **Always review** `migrate.csx` before running \u2014 it executes arbitrary code on your machine");
        report.AppendLine("- **Diff the output** against the validation evidence in `validation/compiled-summary.md`");
        report.AppendLine("- **Check PR links** \u2014 every change should trace back to a real GitHub PR");

        await File.WriteAllTextAsync(reportPath, report.ToString());
    }
}
