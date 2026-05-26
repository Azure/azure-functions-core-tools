// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands.Setup;

internal sealed class SetupRenderer(IInteractionService interaction, SetupOutputMode outputMode)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly SetupOutputMode _outputMode = outputMode;

    public void SetupStarted(
        SetupCommandOptions options,
        SetupFeaturePlan featurePlan,
        IReadOnlyList<SetupProfileScope> profileScopes)
    {
        if (_outputMode == SetupOutputMode.Json)
        {
            WriteEvent("setup.started", new Dictionary<string, object?>
            {
                ["features"] = featurePlan.Features,
                ["worker_runtimes"] = featurePlan.WorkerRuntimes,
                ["profiles"] = profileScopes.Select(scope => scope.Profile?.Name).Where(name => name is not null).ToArray(),
                ["source"] = options.Source,
                ["install_policy"] = ToPolicyText(options.InstallPolicy),
                ["check"] = options.Check,
                ["prerelease"] = options.IncludePrerelease,
            });
            return;
        }

        _interaction.WriteTitle(options.Check ? "Checking Azure Functions setup" : "Setting up Azure Functions");
    }

    public void ProfileStarted(SetupProfileScope profileScope)
    {
        if (_outputMode == SetupOutputMode.Json)
        {
            WriteEvent("profile.started", new Dictionary<string, object?>
            {
                ["profile"] = profileScope.Profile?.Name,
            });
            return;
        }

        if (profileScope.Profile is not null)
        {
            _interaction.WriteSectionHeader($"Profile {profileScope.Profile.Name}");
        }
    }

    public void DependencyDetected(SetupProfileScope profileScope, SetupDependency dependency)
    {
        if (_outputMode != SetupOutputMode.Json)
        {
            return;
        }

        WriteEvent("dependency.detected", DependencyPayload(profileScope, dependency));
    }

    public void DependencyResult(SetupProfileScope profileScope, SetupDependency dependency, SetupDependencyResult result)
    {
        if (_outputMode == SetupOutputMode.Json)
        {
            Dictionary<string, object?> payload = DependencyPayload(profileScope, dependency);
            payload["status"] = ToStatusText(result.Status);
            payload["package_id"] = result.PackageId ?? dependency.ResolvedPackageId ?? dependency.PackageId;
            payload["version"] = result.Version;
            payload["message"] = result.Message;
            WriteEvent("dependency.result", payload);
            return;
        }

        switch (result.Status)
        {
            case SetupDependencyStatus.Satisfied:
                _interaction.WriteSuccess(result.Message);
                break;

            case SetupDependencyStatus.Installed:
                _interaction.WriteSuccess(result.Message);
                break;

            case SetupDependencyStatus.SatisfiedFallback:
                _interaction.WriteWarning(result.Message);
                break;

            case SetupDependencyStatus.Failed:
                _interaction.WriteError(result.Message);
                break;
        }
    }

    public void ProfileCompleted(SetupProfileScope profileScope, ProfileSetupOutcome outcome)
    {
        if (_outputMode == SetupOutputMode.Json)
        {
            WriteEvent("profile.completed", new Dictionary<string, object?>
            {
                ["profile"] = profileScope.Profile?.Name,
                ["success"] = outcome.FailureCount == 0,
                ["failure_count"] = outcome.FailureCount,
            });
        }
    }

    public void SetupCompleted()
    {
        if (_outputMode == SetupOutputMode.Json)
        {
            WriteEvent("setup.completed", new Dictionary<string, object?>
            {
                ["success"] = true,
            });
            return;
        }

        _interaction.WriteSuccess("Azure Functions setup is complete.");
    }

    public void SetupFailed(int failureCount)
    {
        if (_outputMode == SetupOutputMode.Json)
        {
            WriteEvent("setup.failed", new Dictionary<string, object?>
            {
                ["failure_count"] = failureCount,
            });
            return;
        }

        _interaction.WriteError(failureCount == 1
            ? "Azure Functions setup failed with 1 issue."
            : $"Azure Functions setup failed with {failureCount} issues.");
    }

    public void SetupFailed(string message)
    {
        if (_outputMode == SetupOutputMode.Json)
        {
            WriteEvent("setup.failed", new Dictionary<string, object?>
            {
                ["message"] = message,
            });
            return;
        }

        _interaction.WriteError(message);
    }

    public void Warning(string message)
    {
        if (_outputMode == SetupOutputMode.Json)
        {
            WriteEvent("setup.warning", new Dictionary<string, object?>
            {
                ["message"] = message,
            });
            return;
        }

        _interaction.WriteWarning(message);
    }

    private void WriteEvent(string eventType, Dictionary<string, object?> payload)
    {
        payload["type"] = eventType;
        payload["timestamp"] = DateTimeOffset.UtcNow;
        _interaction.WriteLine(JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private static Dictionary<string, object?> DependencyPayload(SetupProfileScope profileScope, SetupDependency dependency)
        => new()
        {
            ["profile"] = profileScope.Profile?.Name,
            ["dependency_type"] = ToDependencyKindText(dependency.Kind),
            ["name"] = dependency.Name,
            ["package_id"] = dependency.ResolvedPackageId ?? dependency.PackageId,
            ["version_range"] = dependency.RangeText,
        };

    private static string ToDependencyKindText(SetupDependencyKind kind)
        => kind switch
        {
            SetupDependencyKind.Host => "host",
            SetupDependencyKind.Worker => "worker",
            SetupDependencyKind.ExtensionBundle => "extension-bundle",
            _ => kind.ToString(),
        };

    private static string ToPolicyText(SetupInstallPolicy policy)
        => policy switch
        {
            SetupInstallPolicy.LatestCompatible => "latest-compatible",
            SetupInstallPolicy.IfNeeded => "if-needed",
            _ => policy.ToString(),
        };

    private static string ToStatusText(SetupDependencyStatus status)
        => status switch
        {
            SetupDependencyStatus.Satisfied => "satisfied",
            SetupDependencyStatus.Installed => "installed",
            SetupDependencyStatus.SatisfiedFallback => "satisfied-fallback",
            SetupDependencyStatus.Failed => "failed",
            _ => status.ToString(),
        };
}
