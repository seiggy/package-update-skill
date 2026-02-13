# Package Update Skill v0.2.0

## Major Refactor: Spectre.Console TUI + Architecture Overhaul

### Spectre.Console Rich Terminal UI
- **Live dashboard**: All 5 pipeline phases render in a single `AnsiConsole.Live()` table that updates in-place — no more scrolling walls of text
- **Progress bars**: Phase 2 (Analyze) shows a `████████░░░░░░ 60%` progress bar with chunk counts
- **Token metrics**: Real-time display of input/output/cached tokens, LLM call count, and duration — sourced from `AssistantUsageEvent`, not estimates
- **Emoji rendering fixed**: `Console.OutputEncoding = UTF8` at startup fixes emoji rendering on Windows Terminal with non-emoji fonts (e.g., JetBrains NerdFont)
- **Spectre emoji shortcodes**: Uses `:check_mark:`, `:warning:`, `:robot:`, `:gear:` etc. instead of raw Unicode emoji for reliable cross-font rendering
- **Icon semantics**: `:robot:` for AI/Copilot SDK operations, `:gear:` for our-code tool calls (NuGet MCP, SDK bootstrap)
- **Rich tool call detail**: Shows what the AI is doing — tool name resolved to human-readable description with argument details (powershell command, URL being fetched, file path being written)

### Architecture: Split Monolithic Program.cs into Focused Classes
The 667-line `Program.cs` has been split into 10 files:

| File | Role |
|------|------|
| `Program.cs` | 11-line entry point: UTF-8 encoding, parse args, run pipeline |
| `PipelineOptions.cs` | CLI argument parsing, validation, derived path properties |
| `PipelineRunner.cs` | Orchestrator: SDK init, Live dashboard, phase sequencing, paranoid validation, cleanup |
| `PhaseRunner.cs` | Copilot session lifecycle: `RunAsync`/`RunWithRetryAsync`, token tracking, tool display |
| `TokenTracker.cs` | Thread-safe token/duration accumulator with K/M formatting |
| `Phases/DiscoveryPhase.cs` | Phase 1: find source repo + list release tags |
| `Phases/AnalyzePhase.cs` | Phase 2: chunked release note analysis |
| `Phases/CompilePhase.cs` | Phase 3: merge/dedup into unified summary |
| `Phases/GeneratePhase.cs` | Phase 4: produce SKILL.md, breakdown docs, migrate.csx |
| `Phases/ReviewPhase.cs` | Phase 5: cross-reference output against evidence |
| `Services/SecurityReportWriter.cs` | Extracted paranoid mode report generation |

### Copilot SDK Integration Improvements
- **Real token tracking**: `AssistantUsageEvent` provides actual input/output/cache-read/cache-write token counts, duration, and LLM call count
- **`SessionConfig.AvailableTools` wiring**: Infrastructure in place for per-phase tool allow-lists to reduce context token usage (tool names TBD pending SDK documentation)
- **Phase-specific tool descriptions**: Each phase maps raw SDK tool names to contextual descriptions (e.g., "Fetching release notes for tag", "Writing skill files", "Reading generated output for audit")
- **Tool argument extraction**: Displays powershell commands, URLs, file paths from tool call arguments

### Copilot Instructions
- Added `.github/copilot-instructions.md` with build/test commands, architecture docs, and coding conventions

## Testing
- **118 unit tests passing** (0 warnings, 0 errors)
- No changes to test files — all existing tests pass against the refactored code

## Installation

### .NET Tool (requires .NET 10 runtime)
```bash
dnx PackageUpdateSkill
```

### Native Binaries (no runtime required)
Pre-built AOT native binaries attached below for:
| Platform | Asset |
|----------|-------|
| Linux x64 | `package-update-skill-linux-x64.tar.gz` |
| Linux ARM64 | `package-update-skill-linux-arm64.tar.gz` |
| macOS ARM64 (Apple Silicon) | `package-update-skill-osx-arm64.tar.gz` |
| Windows x64 | `package-update-skill-win-x64.zip` |
| Windows ARM64 | `package-update-skill-win-arm64.zip` |

## Full Changelog

https://github.com/seiggy/package-update-skill/compare/v0.1.2...v0.2.0
