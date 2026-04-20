// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Parent command for host management: func host [install|list|use|remove].
/// </summary>
public class HostCommand : BaseCommand
{
    private readonly IInteractionService _interaction;
    private readonly IHostManager _hostManager;

    public HostCommand(IInteractionService interaction, IHostManager hostManager, IHostResolver hostResolver)
        : base("host", "Manage Azure Functions host runtime versions.")
    {
        _interaction = interaction;
        _hostManager = hostManager;

        // Show current default in help text
        var defaultVersion = hostManager.GetDefaultVersion();
        if (defaultVersion is not null)
        {
            Description = $"Manage Azure Functions host runtime versions. (Current default: {defaultVersion})";
        }

        Subcommands.Add(new HostInstallCommand(interaction, hostManager));
        Subcommands.Add(new HostListCommand(interaction, hostManager));
        Subcommands.Add(new HostUseCommand(interaction, hostManager));
        Subcommands.Add(new HostRemoveCommand(interaction, hostManager));
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var installed = _hostManager.GetInstalledVersions();
        var defaultVersion = _hostManager.GetDefaultVersion();

        _interaction.WriteBlankLine();

        if (installed.Count == 0)
        {
            _interaction.WriteMarkupLine("[yellow]No host versions installed.[/]");
            _interaction.WriteMarkupLine($"Install one with: [green]func host install {KnownHostVersions.RecommendedVersion}[/]");
        }
        else
        {
            if (defaultVersion is not null)
            {
                var isVerified = KnownHostVersions.IsVerified(defaultVersion);
                var tag = isVerified ? "[green](verified)[/]" : "[yellow](unverified)[/]";
                _interaction.WriteMarkupLine($"[bold]Default host:[/] {defaultVersion} {tag}");
            }
            else
            {
                _interaction.WriteMarkupLine("[yellow]No default host version set.[/]");
                _interaction.WriteMarkupLine($"Set one with: [green]func host use <version>[/]");
            }

            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine($"[grey]{installed.Count} version(s) installed. Run [white]func host list[/] to see all.[/]");
        }

        _interaction.WriteBlankLine();
        return Task.FromResult(0);
    }
}

/// <summary>
/// Handles 'func host install &lt;version&gt;' — downloads and installs a host version from NuGet.
/// </summary>
public class HostInstallCommand : BaseCommand
{
    public static readonly Argument<string> VersionArgument = new("version")
    {
        Description = "The host version to install (e.g., 4.1049.0)"
    };

    private readonly IInteractionService _interaction;
    private readonly IHostManager _hostManager;

    public HostInstallCommand(IInteractionService interaction, IHostManager hostManager)
        : base("install", "Download and install a host runtime version.")
    {
        _interaction = interaction;
        _hostManager = hostManager;

        Arguments.Add(VersionArgument);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var version = parseResult.GetValue(VersionArgument)!;

        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine($"[bold]Installing host version {version}...[/]");
        _interaction.WriteBlankLine();

        string? result = null;

        await _interaction.StatusAsync(
            "Starting...",
            async ct =>
            {
                var reporter = new Progress<HostInstallProgress>(p =>
                {
                    // Progress reporting is handled by the StatusAsync spinner
                });
                result = await _hostManager.InstallAsync(version, reporter, ct);
            },
            cancellationToken);

        _interaction.WriteBlankLine();

        if (result is null)
        {
            return 1;
        }

        // Smoke test: try starting the host briefly
        var smokeTestPassed = false;
        await _interaction.StatusAsync(
            "Running smoke test...",
            ct =>
            {
                smokeTestPassed = RunSmokeTest(result, version);
                return Task.CompletedTask;
            },
            cancellationToken);

        _interaction.WriteBlankLine();

        if (smokeTestPassed)
        {
            _interaction.WriteSuccess($"Host version {version} installed and verified successfully.");
        }
        else
        {
            _interaction.WriteWarning($"Host version {version} installed, but the smoke test did not pass.");
            _interaction.WriteMarkupLine("[yellow]The host may still work for your project. If you encounter issues, try a different version.[/]");
        }

        if (_hostManager.GetDefaultVersion() is null)
        {
            _hostManager.SetDefaultVersion(version);
            _interaction.WriteMarkupLine($"[grey]Set {version} as the default host version.[/]");
        }

        _interaction.WriteBlankLine();
        return 0;
    }

    private static bool RunSmokeTest(string hostDllPath, string version)
    {
        try
        {
            var smokeDir = Path.Combine(Path.GetTempPath(), $"func-smoke-{Guid.NewGuid():N}");
            Directory.CreateDirectory(smokeDir);
            Directory.CreateDirectory(Path.Combine(smokeDir, "workers"));
            File.WriteAllText(Path.Combine(smokeDir, "host.json"), """{"version":"2.0"}""");

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = smokeDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add("exec");

                var hostDir = Path.GetDirectoryName(hostDllPath) ?? "";

                var depsFile = Path.Combine(hostDir, NuGetHostManager.HostDepsFileName);
                if (File.Exists(depsFile))
                {
                    startInfo.ArgumentList.Add("--depsfile");
                    startInfo.ArgumentList.Add(depsFile);
                }

                var runtimeConfig = Path.Combine(hostDir, NuGetHostManager.HostRuntimeConfigFileName);
                if (File.Exists(runtimeConfig))
                {
                    startInfo.ArgumentList.Add("--runtimeconfig");
                    startInfo.ArgumentList.Add(runtimeConfig);
                }

                startInfo.ArgumentList.Add(hostDllPath);

                startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
                startInfo.Environment["FUNCTIONS_CORETOOLS_ENVIRONMENT"] = "true";
                startInfo.Environment["AZURE_FUNCTIONS_ENVIRONMENT"] = "Development";
                startInfo.Environment["AzureWebJobsScriptRoot"] = smokeDir;

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process is null) return false;

                var exited = process.WaitForExit(TimeSpan.FromSeconds(8));

                if (exited)
                {
                    return process.ExitCode == 0;
                }

                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return true;
            }
            finally
            {
                try { Directory.Delete(smokeDir, true); } catch { /* best effort */ }
            }
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Handles 'func host list' — lists installed and optionally available host versions.
/// </summary>
public class HostListCommand : BaseCommand
{
    public static readonly Option<bool> AvailableOption = new("--available", "-a")
    {
        Description = "Show available versions from NuGet (requires internet)"
    };

    private readonly IInteractionService _interaction;
    private readonly IHostManager _hostManager;

    public HostListCommand(IInteractionService interaction, IHostManager hostManager)
        : base("list", "List installed host runtime versions.")
    {
        _interaction = interaction;
        _hostManager = hostManager;

        Options.Add(AvailableOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var showAvailable = parseResult.GetValue(AvailableOption);

        _interaction.WriteBlankLine();

        var installed = _hostManager.GetInstalledVersions();

        if (installed.Count == 0)
        {
            _interaction.WriteMarkupLine("[yellow]No host versions installed.[/]");
            _interaction.WriteMarkupLine($"Install one with: [green]func host install {KnownHostVersions.RecommendedVersion}[/]");
        }
        else
        {
            _interaction.WriteMarkupLine("[bold]Installed host versions:[/]");
            _interaction.WriteBlankLine();

            foreach (var version in installed)
            {
                var markers = new List<string>();
                if (version.IsDefault) markers.Add("[blue](default)[/]");
                if (version.IsKnownGood) markers.Add("[green](verified)[/]");
                if (!version.IsKnownGood) markers.Add("[yellow](unverified)[/]");

                var markerStr = markers.Count > 0 ? " " + string.Join(" ", markers) : "";
                _interaction.WriteMarkupLine($"  {version.Version}{markerStr}");
            }
        }

        if (showAvailable)
        {
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("[bold]Available versions (from NuGet):[/]");
            _interaction.WriteBlankLine();

            try
            {
                var available = await _hostManager.GetAvailableVersionsAsync(cancellationToken);
                var top = available.Take(20).ToList();

                foreach (var version in top)
                {
                    var isInstalled = installed.Any(i =>
                        string.Equals(i.Version, version, StringComparison.OrdinalIgnoreCase));
                    var isVerified = KnownHostVersions.IsVerified(version);

                    var markers = new List<string>();
                    if (isInstalled) markers.Add("[blue](installed)[/]");
                    if (isVerified) markers.Add("[green](verified)[/]");

                    var markerStr = markers.Count > 0 ? " " + string.Join(" ", markers) : "";
                    _interaction.WriteMarkupLine($"  {version}{markerStr}");
                }

                if (available.Count > 20)
                {
                    _interaction.WriteMarkupLine($"  [grey]... and {available.Count - 20} more[/]");
                }
            }
            catch (Exception ex)
            {
                _interaction.WriteError($"Failed to fetch available versions: {ex.Message}");
            }
        }

        _interaction.WriteBlankLine();
        return 0;
    }
}

/// <summary>
/// Handles 'func host use &lt;version&gt;' — sets the default host version.
/// </summary>
public class HostUseCommand : BaseCommand
{
    public static readonly Argument<string> VersionArgument = new("version")
    {
        Description = "The host version to set as default"
    };

    private readonly IInteractionService _interaction;
    private readonly IHostManager _hostManager;

    public HostUseCommand(IInteractionService interaction, IHostManager hostManager)
        : base("use", "Set the default host runtime version.")
    {
        _interaction = interaction;
        _hostManager = hostManager;

        Arguments.Add(VersionArgument);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var version = parseResult.GetValue(VersionArgument)!;

        var installed = _hostManager.GetInstalledVersions();
        var found = installed.FirstOrDefault(v =>
            string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));

        if (found is null)
        {
            _interaction.WriteError($"Host version '{version}' is not installed. Install it first with: func host install {version}");
            return Task.FromResult(1);
        }

        _hostManager.SetDefaultVersion(version);
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine($"[green]✓[/] Default host version set to [bold]{version}[/].");
        _interaction.WriteBlankLine();
        return Task.FromResult(0);
    }
}

/// <summary>
/// Handles 'func host remove &lt;version&gt;' — removes an installed host version.
/// </summary>
public class HostRemoveCommand : BaseCommand
{
    public static readonly Argument<string> VersionArgument = new("version")
    {
        Description = "The host version to remove"
    };

    private readonly IInteractionService _interaction;
    private readonly IHostManager _hostManager;

    public HostRemoveCommand(IInteractionService interaction, IHostManager hostManager)
        : base("remove", "Remove an installed host runtime version.")
    {
        _interaction = interaction;
        _hostManager = hostManager;

        Arguments.Add(VersionArgument);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var version = parseResult.GetValue(VersionArgument)!;

        _interaction.WriteBlankLine();

        if (_hostManager.Remove(version))
        {
            _interaction.WriteMarkupLine($"[green]✓[/] Host version [bold]{version}[/] removed.");
        }
        else
        {
            return Task.FromResult(1);
        }

        _interaction.WriteBlankLine();
        return Task.FromResult(0);
    }
}
