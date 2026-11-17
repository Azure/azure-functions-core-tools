# 02-upgrade-projects-and-packages: Retarget all projects to net11.0 and bump central packages

Change `<TargetFramework>` from `net10.0` to `net11.0` in all 27 `net10.0` projects in `Azure.Functions.Cli.slnx`. Also change `test/Func.Tests/Console/Terminal/Probe/Terminal.Native.Probe.csproj`, which is not in the slnx but is built by Func.Tests. `src/Workloads/Sdk/Workloads.Sdk.csproj` stays on `netstandard2.0` because it is an MSBuild task library. Bump `LangVersion` from 14 to 15 in `eng/build/Engineering.props`.

In `eng/build/Packages.props`, move these packages to their .NET 11 versions:
- Microsoft.Extensions.* (DependencyInjection, DependencyInjection.Abstractions, FileSystemGlobbing, Hosting, Http, Options.DataAnnotations, Logging, Logging.Console): to 11.0.0
- System.Diagnostics.EventLog: to 11.0.0
- Microsoft.TemplateEngine.* (3 packages): to the 11.0.1xx band

Keep System.CommandLine on 2.0.x. Do not bump System.Text.Json or System.Reflection.MetadataLoadContext for `Workloads.Sdk`. If those pins are shared with net11 projects, keep `Workloads.Sdk`'s effective versions unchanged, for example by using a conditional or project-scoped override. Leave all other pins as they are (OpenTelemetry, NuGet.*, Spectre, WebJobs.Script.WebHost, xunit, MSBuild, workers). The assessment rates them compatible and reports no vulnerabilities.

Then restore, build, and fix all compilation errors and warnings in one bounded pass. The build runs with `CI=true`, so warnings are errors. Fix them at the source and never suppress them. Expected friction:
- 22 source-incompatible notices, where `Path.Combine`, `Task.WhenAll`, `string.Join`, and `string.Split` now bind to `ReadOnlySpan` params overloads. Watch for overload ambiguity.
- New analyzer or C# 15 warnings.

The 475 behavioral notices are advisory review items, not required changes. They are mostly `System.Uri`, `JsonDocument`, `HttpContent`, and `ProcessStartInfo.Environment`, concentrated in Func and Func.Tests. Act on one only if a build or test failure points to it. See `assessment.md` and `supporting-files-inventory.md` for detail, and use `query_dotnet_assessment` for per-instance locations.

Out of scope: product behavior for user projects that `func init` generates (`DotNetProjectInitializer` DefaultFramework/SupportedFrameworks and its tests). Opaque test data such as `WorkloadStoreTests` `lib/net10.0/Foo.dll` also stays unchanged.

**Done when**: every in-scope project (including Terminal.Native.Probe) targets `net11.0`, and Workloads.Sdk still targets `netstandard2.0` with unchanged effective System.Text.Json/MetadataLoadContext versions. Central pins are updated as described and LangVersion is 15. `dotnet restore` succeeds. A Release build of `Azure.Functions.Cli.slnx` with `CI=true` produces 0 errors and 0 warnings, with no new suppressions. The existing unit test projects pass.
