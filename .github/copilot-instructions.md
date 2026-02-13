# Copilot Instructions

## Build & Test

```bash
# Restore, build, test (unit tests only — excludes integration tests)
dotnet build --configuration Release
dotnet test --configuration Release --filter "Category!=Integration"

# Run a single test by fully-qualified name
dotnet test --configuration Release --filter "FullyQualifiedName~ContentSanitizerValidationTests.ValidatePackageName_AcceptsValidNames"

# Pack as a .NET tool
dotnet pack PackageUpdateSkill --configuration Release --output ./nupkg
```

Requires **.NET 10 SDK** (preview). CI installs via `dotnet-version: 10.0.x` with `dotnet-quality: preview`.

## Architecture

This is a 5-phase AI pipeline that analyzes NuGet package upgrades and generates GitHub Copilot migration skills. The phases run sequentially as isolated Copilot SDK sessions:

1. **Discovery** → find source repo + list release tags
2. **Analyze** → fetch release notes per version (chunked in groups of 5)
3. **Compile** → merge/deduplicate findings into a unified summary
4. **Generate** → produce SKILL.md, breakdown docs, and a Roslyn `migrate.csx` script
5. **Review** → cross-reference output against source evidence, fix hallucinations

### Project layout

| File | Role |
|------|------|
| `Program.cs` | Thin entry point: sets UTF-8 encoding, parses args, runs pipeline. |
| `PipelineOptions.cs` | Immutable CLI options with validation and derived path properties. |
| `PipelineRunner.cs` | Orchestrator: initializes Copilot SDK, runs phases inside a `AnsiConsole.Live()` dashboard with progress bars and token metrics, handles paranoid validation, cleanup. |
| `PhaseRunner.cs` | Copilot session manager: `RunAsync` / `RunWithRetryAsync` create sessions, capture output, report tool calls via `Action<string>` callbacks, track real token usage from `AssistantUsageEvent`. |
| `TokenTracker.cs` | Thread-safe token/cost/duration accumulator. Formats counts with K/M suffixes (max 3 digits). Fed by `AssistantUsageEvent` data. |
| `Phases/DiscoveryPhase.cs` | Phase 1 prompts + result parsing (repo ref, version list). |
| `Phases/AnalyzePhase.cs` | Phase 2 prompts + chunked execution + analysis gathering. |
| `Phases/CompilePhase.cs` | Phase 3 prompts + merge/dedup. |
| `Phases/GeneratePhase.cs` | Phase 4 prompts + post-processing (SkillWriter, file copying). |
| `Phases/ReviewPhase.cs` | Phase 5 prompts + cross-reference audit. |

### Services (`PackageUpdateSkill/Services/`)

| Service | Role |
|---------|------|
| `ContentSanitizer` | Static. Regex-based input validation and injection scanning. Validates package names/versions, truncates content, wraps untrusted content in `<untrusted-content>` tags. |
| `ContentValidator` | LLM-powered semantic injection detection (paranoid mode). Runs in tool-denied Copilot sessions for pure text analysis. |
| `SkillWriter` | Assembles YAML frontmatter + markdown body into `.copilot/skills/<slug>/SKILL.md`. |
| `CopilotCliBootstrap` | Downloads and caches the Copilot CLI binary from npm on first run when the SDK binary isn't bundled. |
| `SecurityReportWriter` | Generates the paranoid mode `security-report.md` from accumulated scan results. |

## Key Conventions

- **Validation tuple pattern**: Validation methods return `(bool IsValid, string? Error)` — check `IsValid` first, use `Error` for the message.
- **Integration tests**: Marked with `[Trait("Category", "Integration")]` and excluded from CI via `--filter "Category!=Integration"`. These require a live Copilot SDK session.
- **Native AOT**: The project is configured with `PublishAot=true`, `IsTrimmable=true`, and `InvariantGlobalization=true`. Avoid APIs incompatible with trimming/AOT.
- **Naming**: NuGet PackageId is `PackageUpdateSkill` (PascalCase); the installed CLI command is `package-update-skill` (kebab-case) via `ToolCommandName`.
- **Source-generated regex**: Use `[GeneratedRegex]` partial methods (see `ContentSanitizer`) instead of `new Regex()` for AOT compatibility.
- **Untrusted content handling**: External content (NuGet READMEs, GitHub release notes) must be wrapped with `ContentSanitizer.WrapUntrustedContent()` before inclusion in LLM prompts. Prompts must include defensive instructions telling the LLM to ignore embedded instructions.
- **Console output**: Use `Spectre.Console.AnsiConsole` for all terminal output — never `Console.Write`/`Console.Error`. Use `:emoji_shortcodes:` (e.g., `:check_mark:`, `:warning:`, `:locked:`, `:gear:`) instead of raw Unicode emoji. Use `MarkupLineInterpolated` for dynamic content to auto-escape markup characters. The pipeline phases render inside `AnsiConsole.Live()` with a table dashboard; pre-pipeline steps (NuGet fetch, SDK init) use `AnsiConsole.Status()`.
- **Phase callbacks**: Phases communicate status via `Action<string>` callbacks, not Spectre `StatusContext`. This decouples phase logic from rendering. `PhaseRunner` resolves tool names to human-readable descriptions via a shared dictionary.
- **Token tracking**: `PhaseRunner` listens for `AssistantUsageEvent` to capture real input/output/cache tokens, cost, and duration. `TokenTracker` formats with K/M suffixes (max 3 significant digits).
- **Testing**: xUnit with `[Fact]` / `[Theory]` + `[InlineData]`. Test project has a global `<Using Include="Xunit" />`.
