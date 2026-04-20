// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Helpers for starting and tagging <see cref="Activity"/> instances used by
/// the CLI. Caller owns the activity's lifetime; these helpers only attach
/// tags. When no listener is subscribed to the underlying
/// <see cref="ActivitySource"/>, <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// returns <c>null</c> and the tagging helpers are skipped.
/// </summary>
public static class ActivityExtensions
{
    private static readonly string _cliVersion = ResolveCliVersion();

    internal static string CliVersion => _cliVersion;

    /// <summary>
    /// Starts an <see cref="Activity"/> that represents the execution of a
    /// CLI command. Returns <c>null</c> when no listener is subscribed.
    /// </summary>
    public static Activity? StartCommandActivity(this ActivitySource source, string commandName)
    {
        var activity = source.StartActivity($"func {commandName}", ActivityKind.Client);
        return activity?.SetCommandTags(commandName);
    }

    /// <summary>
    /// Tags the activity with the command name and the standard CLI
    /// environment tags (version, OS, runtime).
    /// </summary>
    public static Activity SetCommandTags(this Activity activity, string commandName)
    {
        activity.SetTag("command.name", commandName);
        activity.SetTag("cli.version", _cliVersion);
        activity.SetTag("os.type", RuntimeInformation.OSDescription);
        activity.SetTag("os.architecture", RuntimeInformation.OSArchitecture.ToString());
        activity.SetTag("runtime.framework", RuntimeInformation.FrameworkDescription);
        return activity;
    }

    /// <summary>
    /// Marks the activity with success or failure status.
    /// </summary>
    public static Activity SetCommandResult(this Activity activity, bool isSuccess, string? errorDescription = null)
    {
        activity.SetStatus(
            isSuccess ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
            isSuccess ? null : errorDescription);
        return activity;
    }

    private static string ResolveCliVersion()
    {
        return typeof(ActivityExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
    }
}
