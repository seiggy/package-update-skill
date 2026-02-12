# 🚀 Package Update Skill v0.1.0

The initial release of **package-update-skill** — a .NET 10 tool that analyzes NuGet package upgrades and generates GitHub Copilot skill files with migration instructions and Roslyn-based code transformation scripts.

## ✨ Features

### 5-Phase AI Pipeline
- **Discovery** — Finds the package's source repo on GitHub and lists all release tags between two versions
- **Analyze** — Fetches and analyzes release notes for each version in chunks, extracting breaking changes, renames, and deprecations
- **Compile** — Merges and deduplicates findings into a unified migration guide
- **Generate** — Produces a SKILL.md, focused breakdown docs, and a Roslyn migration script
- **Review** — Cross-references all output against source evidence to catch hallucinations

### Copilot Skill Output
Generates a complete skill package to `.copilot/skills/<package>-migration/`:
- `SKILL.md` with YAML frontmatter
- Category breakdown docs (breaking changes, API renames, deprecations, etc.)
- `scripts/migrate.csx` — automated Roslyn-based code transformation script

### Powered by GitHub Copilot SDK
- Uses your Copilot subscription — no Azure OpenAI deployment needed
- `--model` flag to choose any supported model (gpt-5, claude-opus-4.6, gpt-5.2-codex, etc.)
- Each pipeline phase runs as an isolated Copilot SDK session with only the tools it needs

### Two-Layer Security (`--paranoid` flag)
- **Layer 1: Regex Fast-Pass** — Scans for 25+ known injection patterns instantly
- **Layer 2: LLM Semantic Analysis** — Dedicated security-analyst session detects obfuscated attacks (Unicode homoglyphs, zero-width chars, base64 encoding, word splitting, indirect injection, code injection)
- Generated migration scripts are reviewed for malicious code patterns (network calls, process spawning, credential theft)
- Security report generated with all findings

### Anti-Hallucination Guardrails
- Phase 2 requires verbatim quoting with PR numbers as evidence
- Phase 3 forbids inventing names or abbreviations
- Phase 5 cross-references output against source evidence

## 📦 Installation

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

## 🛠️ Usage

```bash
package-update-skill <PackageName> <FromVersion> <ToVersion> [--model <model>] [--dir <repoDir>] [--paranoid] [--debug]
```

### Example
```bash
# Analyze Microsoft Agent Framework upgrade and generate migration skill
package-update-skill Microsoft.Agents.AI.OpenAI 1.0.0-preview.251007.1 1.0.0-preview.260209.1 --model claude-opus-4.6

# Run the generated migration script against your codebase
dotnet script .copilot/skills/microsoft-agents-ai-openai-migration/scripts/migrate.csx
```

## 🧪 Testing

- **118 unit tests** — Input validation, content sanitization, red teaming (injection detection, path traversal, YAML injection), pipeline helpers
- **14 integration tests** — Prove regex-based sanitizer misses sophisticated attacks (homoglyphs, zero-width chars, base64, word splitting, HTML entities, hidden code exfil) while the LLM-based validator catches all of them

## ⚠️ Important Notes

- **Always review `migrate.csx` before running** — it generates executable code
- **Use `--paranoid` for unfamiliar packages** — especially community packages you haven't vetted
- The `--debug` flag retains intermediate working files for inspection

## Full Changelog

https://github.com/seiggy/package-update-skill/commits/v0.1.0
