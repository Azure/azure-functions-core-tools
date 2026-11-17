# 01-sdk-toolchain: Point the repository at the .NET 11 SDK

Update `global.json` so the build uses the installed .NET 11 SDK. Set `sdk.version` to `11.0.100` and keep the existing `rollForward: latestMinor` and `allowPrerelease` settings. Do not run `validate_dotnet_sdk_in_globaljson`, because it rewrites `rollForward` to `latestMajor`. Leave `msbuild-sdks` (NoTargets 3.7.56, Traversal 4.1.0) unchanged. The CI templates (`UseDotNet@2 useGlobalJson: true`) and `.github/workflows/terminal-macos.yml` (`global-json-file`) pick up the SDK from this file.

This task must land before any `net11.0` build is attempted. The pinned 10.x SDK cannot target `net11.0`, and `latestMinor` will not roll forward to 11.

**Done when**: `global.json` pins `11.0.100` with its rollForward policy unchanged, and `dotnet --version` in the repo root resolves to an 11.0.x SDK.

## Research findings (executor, before edit)

- **Only `global.json` in scope**: one file at repo root (no nested global.json outside out/bin/obj).
- **Current contents (observed)**: `sdk.version` `10.0.403`, `allowPrerelease: true`, `rollForward: latestMinor`; `msbuild-sdks` NoTargets `3.7.56`, Traversal `4.1.0`.
- **Installed SDKs (`dotnet --list-sdks`)**: 6.0.100, 6.0.428, 7.0.410, 8.0.300, 8.0.428, 9.0.321, 10.0.115, 10.0.403, 10.0.404, **11.0.100**. Pre-change `dotnet --version` at repo root = `10.0.404` (latestMinor roll from 10.0.403).
- **Consumers (confirmed)**: `eng/ci/templates/steps/setup-dependencies.yml` (`UseDotNet@2 useGlobalJson: true`) and `.github/workflows/terminal-macos.yml:63` (`global-json-file: global.json`) — no edits needed there for this task.
- **Change to apply**: set `sdk.version` → `11.0.100`; leave `allowPrerelease`, `rollForward`, `msbuild-sdks` untouched. Do NOT run `validate_dotnet_sdk_in_globaljson`.
- **Validation**: `dotnet --version` at repo root must report `11.0.x`. No project build (net11.0 retarget is task 02).
- **Decomposition**: single-file, single-concern change → atomic (common.md hints `multi-project-dependency-ordering`, `large-package-replacement-batch` do not match; no test/framework flavors in scope).
