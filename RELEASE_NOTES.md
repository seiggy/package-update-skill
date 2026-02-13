# 🚀 Package Update Skill v0.1.2

## 🐛 Fixes

### NuGet Package Size — Copilot CLI Runtime Bootstrap
The v0.1.1 NuGet package exceeded NuGet.org's 250 MB size limit (343 MB) because all 6 platform-specific Copilot CLI binaries (~55–130 MB each) were bundled inside the tool package.

**v0.1.2 introduces a runtime bootstrap** that downloads only the binary needed for your platform on first run:

- **First run**: Detects OS/architecture, downloads the correct Copilot CLI from npm, and caches it locally at `%LOCALAPPDATA%/package-update-skill/copilot-cli/` (Windows) or `~/.local/share/package-update-skill/copilot-cli/` (Linux/macOS)
- **Subsequent runs**: Uses the cached binary — no network call needed
- **NuGet package size**: 7.9 MB (down from 343 MB)

Supported platforms: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`

### MSBuild Target Cleanup
- Removed the `_DownloadAllCopilotClis` multi-platform bundling target from the `.csproj`
- Added `_ExcludeCopilotCliFromToolPack` target that strips the build-time CLI binary from the publish output during `dotnet pack`, ensuring the NuGet tool package stays small

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

## Full Changelog

https://github.com/seiggy/package-update-skill/compare/v0.1.1...v0.1.2
