// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands.Setup;

/// <summary>
/// Prepares the local machine for running Azure Functions projects.
/// </summary>
internal sealed class SetupCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string[]?> FeaturesOption { get; } = new("--features")
    {
        Description =
            "Components to install. Repeatable or comma-separated. One of:\n"
            + "  node | python | go | dotnet   full dev setup for the language (host, worker if any, stack, bundle)\n"
            + "  runtime                       host + extension bundle\n"
            + "  host                          host only",
        Arity = ArgumentArity.OneOrMore,
    };

    public Option<string[]?> ProfileOption { get; } = new("--profile")
    {
        Description = "Azure Functions profile to use for version constraints. May be specified multiple times.",
        Arity = ArgumentArity.OneOrMore,
    };

    public Option<string?> ProfilesOption { get; } = new("--profiles")
    {
        Description = "Comma-separated Azure Functions profiles to use for version constraints.",
    };

    public Option<string?> InstallPolicyOption { get; } = new("--install-policy")
    {
        Description = "Install policy: latest-compatible or if-needed. Default: latest-compatible.",
    };

    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "NuGet package source to use for workload resolution and installation.",
    };

    public Option<bool> PrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Allow prerelease workload versions when resolving from the catalog.",
    };

    public Option<bool> NonInteractiveOption { get; } = new("--non-interactive")
    {
        Description = "Do not prompt for input.",
    };

    public Option<bool> YesOption { get; } = new("--yes", "-y")
    {
        Description = "Answer yes to setup prompts.",
    };

    public Option<bool> CheckOption { get; } = new("--check")
    {
        Description = "Check whether the selected dependencies are installed without making changes.",
    };

    public Option<string?> OutputOption { get; } = new("--output")
    {
        Description = "Output mode: plain or json (NDJSON). Default: plain.",
    };

    private readonly ISetupRunner _runner;

    public SetupCommand(ISetupRunner runner)
        : base("setup", "Prepare local Azure Functions dependencies.")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));

        AddPathArgument();
        Options.Add(FeaturesOption);
        Options.Add(ProfileOption);
        Options.Add(ProfilesOption);
        Options.Add(InstallPolicyOption);
        Options.Add(SourceOption);
        Options.Add(PrereleaseOption);
        Options.Add(NonInteractiveOption);
        Options.Add(YesOption);
        Options.Add(CheckOption);
        Options.Add(OutputOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        if (!workingDirectory.Exists)
        {
            string displayPath = workingDirectory.OriginalPath ?? workingDirectory.Info.FullName;
            throw new GracefulException($"The specified path does not exist: '{displayPath}'", isUserError: true);
        }

        SetupCommandOptions options = new(
            workingDirectory.Info,
            SplitList(parseResult.GetValue(FeaturesOption)),
            SplitList(parseResult.GetValue(ProfileOption)).Concat(SplitList(parseResult.GetValue(ProfilesOption))).ToArray(),
            NullIfWhiteSpace(parseResult.GetValue(SourceOption)),
            ResolveInstallPolicy(parseResult.GetValue(InstallPolicyOption)),
            parseResult.GetValue(PrereleaseOption),
            parseResult.GetValue(NonInteractiveOption),
            parseResult.GetValue(YesOption),
            parseResult.GetValue(CheckOption),
            ResolveOutputMode(parseResult.GetValue(OutputOption)));

        SetupRunResult result = await _runner.RunAsync(options, cancellationToken);
        return result.ExitCode;
    }

    private static SetupInstallPolicy ResolveInstallPolicy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SetupInstallPolicy.LatestCompatible;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "latest-compatible" => SetupInstallPolicy.LatestCompatible,
            "if-needed" => SetupInstallPolicy.IfNeeded,
            _ => throw new GracefulException(
                $"--install-policy must be one of: latest-compatible, if-needed. Got '{raw}'.",
                isUserError: true),
        };
    }

    private static SetupOutputMode ResolveOutputMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SetupOutputMode.Plain;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "plain" => SetupOutputMode.Plain,
            "json" => SetupOutputMode.Json,
            _ => throw new GracefulException("--output must be one of: plain, json. Got '" + raw + "'.", isUserError: true),
        };
    }

    private static IReadOnlyList<string> SplitList(string[]? values)
        => values is null ? [] : SplitList(values.AsEnumerable());

    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value) ? [] : SplitList([value]);

    private static IReadOnlyList<string> SplitList(IEnumerable<string> values)
    {
        List<string> result = [];
        foreach (string value in values)
        {
            foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    result.Add(part);
                }
            }
        }

        return result;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
