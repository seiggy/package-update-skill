using System.Text;
using GitHub.Copilot.SDK;
using Spectre.Console;

namespace PackageUpdateSkill;

/// <summary>
/// Manages Copilot SDK sessions for pipeline phases.
/// Captures LLM output, updates Spectre Status spinners on tool calls.
/// </summary>
public class PhaseRunner(CopilotClient copilot, PipelineOptions options, TokenTracker tokens)
{
    /// <summary>
    /// Maps raw Copilot SDK tool names to human-readable descriptions.
    /// Phases can pass additional overrides via the toolDisplayName callback.
    /// </summary>
    private static readonly Dictionary<string, string> ToolDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // GitHub search / repos
        ["search_repositories"] = "Searching GitHub repositories",
        ["search_code"] = "Searching code",
        ["get_repository"] = "Fetching repository info",

        // Releases & tags
        ["list_releases"] = "Listing releases",
        ["get_release"] = "Fetching release details",
        ["list_tags"] = "Listing tags",
        ["list_repository_tags"] = "Listing repository tags",
        ["get_release_by_tag"] = "Fetching release by tag",

        // File operations
        ["get_file_contents"] = "Reading file",
        ["read_file"] = "Reading file",
        ["write_file"] = "Writing file",
        ["create_or_update_file"] = "Writing file",
        ["list_directory"] = "Listing directory",

        // NuGet MCP
        ["get-package-readme"] = "Fetching NuGet README",
        ["get-package-metadata"] = "Fetching package metadata",
        ["search-packages"] = "Searching NuGet packages",
    };

    private static string ResolveToolDisplay(string toolName, Func<string, string?>? overrides)
    {
        var display = overrides?.Invoke(toolName);
        if (display != null)
            return display;

        if (ToolDescriptions.TryGetValue(toolName, out var description))
            return description;

        return toolName.Replace('_', ' ');
    }

    /// <summary>
    /// Extracts a short detail string from tool arguments for display.
    /// Returns null if no useful detail can be extracted.
    /// </summary>
    private static string? ExtractToolDetail(string toolName, object? arguments)
    {
        if (arguments == null) return null;

        try
        {
            // Arguments is typically a JsonElement or Dictionary
            var json = arguments.ToString() ?? "";

            return toolName switch
            {
                "powershell" or "shell" or "bash" or "execute_command" =>
                    ExtractJsonField(json, "command", maxLen: 60),
                "web_fetch" or "fetch" or "http_request" =>
                    ExtractJsonField(json, "url", maxLen: 80),
                "create" or "write_file" or "create_or_update_file" =>
                    ExtractJsonField(json, "path", maxLen: 80) ?? ExtractJsonField(json, "file_path", maxLen: 80),
                "view" or "read_file" or "get_file_contents" =>
                    ExtractJsonField(json, "path", maxLen: 80) ?? ExtractJsonField(json, "file_path", maxLen: 80),
                "grep" or "search" =>
                    ExtractJsonField(json, "pattern", maxLen: 60) ?? ExtractJsonField(json, "query", maxLen: 60),
                "list_directory" =>
                    ExtractJsonField(json, "path", maxLen: 80),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractJsonField(string json, string fieldName, int maxLen)
    {
        // Simple extraction without requiring System.Text.Json dependency in hot path
        var key = $"\"{fieldName}\"";
        var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        // Find the value after the key
        var colonIdx = json.IndexOf(':', idx + key.Length);
        if (colonIdx < 0) return null;

        // Skip whitespace and opening quote
        var valStart = colonIdx + 1;
        while (valStart < json.Length && (json[valStart] == ' ' || json[valStart] == '"'))
            valStart++;

        if (valStart >= json.Length) return null;

        // Find end of value (next quote or comma/brace)
        var valEnd = valStart;
        while (valEnd < json.Length && json[valEnd] != '"' && json[valEnd] != ',' && json[valEnd] != '}')
            valEnd++;

        var value = json[valStart..valEnd].Trim();
        if (string.IsNullOrEmpty(value)) return null;

        // Truncate long values
        if (value.Length > maxLen)
            value = value[..(maxLen - 3)] + "...";

        return value;
    }

    /// <summary>
    /// Runs a single Copilot session with the given prompts.
    /// LLM streaming output is captured (not printed). Tool calls update the Status spinner.
    /// </summary>
    /// <param name="toolDisplayName">
    /// Optional callback that maps a raw tool name to a human-readable description.
    /// Return null to fall back to the raw tool name.
    /// </param>
    public async Task<string> RunAsync(
        string systemMessage,
        string prompt,
        Action<string>? onToolCall = null,
        Func<string, string?>? toolDisplayName = null,
        List<string>? availableTools = null)
    {
        var output = new StringBuilder();
        var done = new TaskCompletionSource();

        var config = new SessionConfig
        {
            Model = options.Model,
            WorkingDirectory = options.WorkDir,
            SystemMessage = new SystemMessageConfig { Content = systemMessage },
            OnPermissionRequest = (_, _) => Task.FromResult(
                new PermissionRequestResult { Kind = "approved" }),
        };
        if (availableTools != null)
            config.AvailableTools = availableTools;

        var session = await copilot.CreateSessionAsync(config);

        try
        {
            session.On(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageEvent msg:
                        output.Append(msg.Data.Content);
                        break;
                    case AssistantUsageEvent usage:
                        tokens.IncrementCalls();
                        if (usage.Data.InputTokens.HasValue)
                            tokens.AddInput((long)usage.Data.InputTokens.Value);
                        if (usage.Data.OutputTokens.HasValue)
                            tokens.AddOutput((long)usage.Data.OutputTokens.Value);
                        if (usage.Data.CacheReadTokens.HasValue)
                            tokens.AddCacheRead((long)usage.Data.CacheReadTokens.Value);
                        if (usage.Data.CacheWriteTokens.HasValue)
                            tokens.AddCacheWrite((long)usage.Data.CacheWriteTokens.Value);
                        if (usage.Data.Cost.HasValue)
                            tokens.AddCost(usage.Data.Cost.Value);
                        if (usage.Data.Duration.HasValue)
                            tokens.AddDuration(usage.Data.Duration.Value);
                        break;
                    case ToolExecutionStartEvent tool:
                        var display = ResolveToolDisplay(tool.Data.ToolName, toolDisplayName);
                        var detail = ExtractToolDetail(tool.Data.ToolName, tool.Data.Arguments);
                        if (detail != null)
                            display = $"{display} — {detail}";
                        onToolCall?.Invoke(display);
                        break;
                    case SessionIdleEvent:
                        done.TrySetResult();
                        break;
                }
            });

            await session.SendAsync(new MessageOptions { Prompt = prompt });
            await done.Task;
        }
        finally
        {
            await session.DisposeAsync();
        }

        return output.ToString();
    }

    /// <summary>
    /// Runs a phase with retry logic. Retries up to maxAttempts if verify() returns false.
    /// </summary>
    public async Task<string> RunWithRetryAsync(
        string systemMessage,
        string prompt,
        Func<bool> verify,
        Action<string>? onToolCall = null,
        int maxAttempts = 3,
        Func<string, string?>? toolDisplayName = null,
        List<string>? availableTools = null)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await RunAsync(systemMessage, prompt, onToolCall, toolDisplayName, availableTools);
            if (verify())
                return result;
            if (attempt < maxAttempts)
            {
                onToolCall?.Invoke($"Retrying ({attempt + 1}/{maxAttempts})...");
                prompt = $"IMPORTANT: You did not write the required files. You MUST write them.\n\n{prompt}";
            }
        }

        return "";
    }
}
