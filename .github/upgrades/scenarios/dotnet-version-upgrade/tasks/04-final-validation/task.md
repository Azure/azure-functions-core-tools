# 04-final-validation: Full clean Release validation of the .NET 11 upgrade

Run the end-to-end validation the user requested on the final tree, after the code, pipeline, and doc changes:
1. Start from a clean state (`dotnet clean` and remove stale `bin/obj` or `out/` outputs as needed).
2. Run `dotnet restore` on `Azure.Functions.Cli.slnx`.
3. Run a Release build with `CI=true`.
4. Run the relevant test projects: Func.Tests, Workloads.Host.Tests, Workloads.Sdk.Tests, and the Workloads stack tests.

Confirm that `Terminal.Native.Probe` builds as part of the Func.Tests run. Any failure found here is fixed at the source and does not get suppressed.

Also confirm there are no `NU1605`/`NU1608` downgrade or conflict warnings and no new vulnerability warnings. `git status` should show only intended changes, with no stray artifacts from the upgrade tooling.

**Done when**: a clean `dotnet restore` succeeds. The Release build with `CI=true` finishes with 0 errors and 0 warnings. All relevant test projects pass, or any failures are shown to be pre-existing or environment-dependent and documented. The working tree contains only intended upgrade changes, all committed.
