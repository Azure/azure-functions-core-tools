# Instructions of Azure Functions Core Tools Versioning

## Goals
- Ensure Core Tools version bumps follow [SemVer](https://semver.org/) consistently.
- Prevent build or release failures caused by incorrect versioning.
- Reduce unnecessary searching when updating versions or preparing releases.

## Limitations
- Keep instructions concise (under two pages).
- Apply only to versioning tasks, not to unrelated development or release activities.

## High-Level Details
- The repository builds **Azure Functions Core Tools**, a CLI for Azure Functions development and operations.
- Languages: primarily **.NET / C#** with MSBuild props files controlling versions.
- Versioning follows **SemVer**:
    - **MAJOR** – breaking changes or major dependency upgrades.
    - **MINOR** – backward-compatible features or minor dependency upgrades.
    - **PATCH** – bug fixes or hotfix-level dependency changes.

## Build Instructions
- Versions must be updated in both **props files** and **release notes**.
- Always run `dotnet restore` before building.
- Build with `dotnet build` from the repo root or specific project folder.
- Run tests with `dotnet test` to validate compatibility.
- After version bumps, verify that builds and tests succeed locally before pushing changes.

**Version bump steps**:
1. **Out-of-proc version**: update `VersionPrefix` in `src/Cli/func/Directory.Version.props`.
3. **In-proc version** (if required): update `VersionPrefix` in `src/Azure.Functions.Cli/Directory.Version.props`.
2. Update `Core Tools` version in `./release_notes.md`.

## Project Layout
- **src/Cli/func/Directory.Version.props** – controls out-of-proc Core Tools version (main branch)
- **src/Azure.Functions.Cli/Directory.Version.props** – controls in-proc Core Tools version (in-proc branch)
- **release_notes.md** – records Core Tools and Host version updates. (only exists in the main branch)
- **src/** – main CLI implementation.
- **eng/** – engineering assets and build props.
- CI pipelines validate builds and tests, and fail if version or release notes are not updated.

## Steps to Follow

- Always update version numbers in the correct `Directory.Version.props` file(s).
- Always update `release_notes.md` in the repo root with the new Core Tools and Host versions.
- Use SemVer rules to decide the version bump:
    - **MAJOR** for breaking changes.
    - **MINOR** for backward-compatible features.
    - **PATCH** for bug fixes or host hotfixes.
- Run `dotnet restore`, `dotnet build`, and `dotnet test` locally after version updates.
- Submit PRs only after validating successful builds and tests.
- Trust these instructions and do not search for versioning guidance elsewhere in the repo.
