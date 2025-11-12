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

Version updates are done in two places: the `main` branch for out-of-proc versions, and the `in-proc` branch for in-proc versions.
The `main` branch controls the out-of-proc host version, while the `in-proc` branch controls the in-proc host version.
You may need to update one or both versions depending on the changes.

1. **Out-of-proc version**:
    - Checkout the `main` branch
    - Update `VersionPrefix` in `src/Cli/func/Directory.Version.props`.
2. **In-proc version** (if requested):
    - Checkout the `in-proc` branch
    - Update `VersionPrefix` in `src/Azure.Functions.Cli/Directory.Version.props`.

### Host Version Update Procedure

Updates happen only when there are changes to the host dependency.
The `main` branch controls the out-of-proc host version, while the `in-proc` branch controls the in-proc host version.
You may need to update one or both host versions depending on the changes.

1. **Out-of-proc version**:
    - Checkout the `main` branch
    - Update the `Microsoft.Azure.WebJobs.Script.WebHost` package version in `eng/build/Packages.props`.
2. **In-proc version** (if requested):
    - Checkout the `in-proc` branch
    - Update the `Microsoft.Azure.WebJobs.Script.WebHost.InProc` package version in `src/Azure.Functions.Cli/Azure.Functions.Cli.csproj`.

### Release Note Update Procedure

Release note updates are only done in the `main` branch.
Update the release notes to reflect the new Core Tools and Host versions after updating them in the respective branches.

1. Update `Core Tools` version in `./release_notes.md`.
2. Update `Host Version` in `./release_notes.md` if the host version was changed.
3. Update `In-Proc Host Version` in `./release_notes.md` if the in-proc host version was changed in the in-proc branch.

### Worker Version Procedure

If the worker versions need to be updated due to host version changes, provide clear instructions in the PR description for users to update their worker versions accordingly.

- User must pull the release prep branch and run the following command:

    ```bash
        # main branch (out-of-proc)
        pwsh ./eng/scripts/validate-worker-versions.ps1 --Update -HostVersion <NewHostVersion>

        # in-proc branch (in-proc)
        pwsh ./validateWorkerVersions.ps1 --Update -HostVersion <NewInProcHostVersion>
    ```

- Replace `<NewHostVersion>` with the requested host version number
- Replace `<NewInProcHostVersion>` with the requested in-proc host version number

## Project Layout
- **src/Cli/func/Directory.Version.props** – controls out-of-proc Core Tools version (main branch)
- **src/Azure.Functions.Cli/Directory.Version.props** – controls in-proc Core Tools version (in-proc branch)
- **release_notes.md** – records Core Tools and Host version updates. (only exists in the main branch)
- **src/** – main CLI implementation.
- **eng/** – engineering assets and build props.
- CI pipelines validate builds and tests, and fail if version or release notes are not updated.

## Steps to Follow

- Always update version numbers in the correct `Directory.Version.props` file(s).
- Always update `release_notes.md` in the repo root with the new Core Tools version.
- If applicable, update Host versions in the correct `Packages.props` or `.csproj` file(s).
- If Host versions are updated, ensure to reflect those changes in `release_notes.md`.
- If a change to in-proc versions is needed, switch to the `in-proc` branch and make the necessary updates.
- Use SemVer rules to decide the version bump:
    - **MAJOR** for breaking changes.
    - **MINOR** for backward-compatible features.
    - **PATCH** for bug fixes or host hotfixes.
- Run `dotnet restore`, `dotnet build`, and `dotnet test` locally after version updates.
- Submit PRs only after validating successful builds and tests.
- Trust these instructions and do not search for versioning guidance elsewhere in the repo.
- If the host version changes: in the PR description, provide clear instructions for a user to update the worker versions.
