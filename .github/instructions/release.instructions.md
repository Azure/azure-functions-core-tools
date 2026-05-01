# Instructions of Azure Functions Core Tools Release Preparation

## Goals
- Ensure Core Tools version bumps follow [SemVer](https://semver.org/) consistently.
- Ensure release notes are updated with each version bump.
- Update the host dependency version when applicable.
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

#### Version Update Procedure

Version updates are done in the `main` branch.

1. **Update version**:
    - Checkout the `main` branch
    - Update `VersionPrefix` in `src/Func/Directory.Version.props`.

### Host Version Update Procedure

Updates happen only when there are changes to the host dependency.

1. **Update host version**:
    - Checkout the `main` branch
    - Update the `Microsoft.Azure.WebJobs.Script.WebHost` package version in `eng/build/Packages.props`.

### Release Note Update Procedure

Release note updates are only done in the `main` branch.
Update the release notes to reflect the new Core Tools and Host versions after updating them in the respective branches.

1. Update `Core Tools` version in `./release_notes.md`.
2. Update `Host Version` in `./release_notes.md` if the host version was changed.

### Worker Version Procedure

If the worker versions need to be updated due to host version changes, provide clear instructions in the PR description for users to update their worker versions accordingly.

- User must pull the release prep branch and run the following command:

    ```bash
        pwsh ./eng/scripts/validate-worker-versions.ps1 --Update -HostVersion <NewHostVersion>
    ```

- Replace `<NewHostVersion>` with the requested host version number

## Project Layout
- **src/Func/Directory.Version.props** – controls Core Tools version
- **release_notes.md** – records Core Tools and Host version updates.
- **src/** – main CLI implementation.
- **eng/** – engineering assets and build props.
- CI pipelines validate builds and tests, and fail if version or release notes are not updated.

## Steps to Follow

- Always update version numbers in `src/Func/Directory.Version.props`.
- Always update `release_notes.md` in the repo root with the new Core Tools version.
- If applicable, update Host versions in `eng/build/Packages.props`.
- If Host versions are updated, ensure to reflect those changes in `release_notes.md`.
- Use SemVer rules to decide the version bump:
    - **MAJOR** for breaking changes.
    - **MINOR** for backward-compatible features.
    - **PATCH** for bug fixes or host hotfixes.
- Run `dotnet restore`, `dotnet build`, and `dotnet test` locally after version updates.
- Submit PRs only after validating successful builds and tests.
- Trust these instructions and do not search for versioning guidance elsewhere in the repo.
- If the host version changes: in the PR description, provide clear instructions for a user to update the worker versions.
