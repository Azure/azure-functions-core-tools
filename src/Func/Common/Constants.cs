// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Common;

internal static class Constants
{
    public const string ProductName = "Azure Functions CLI";
    public const string CliName = "func";
    public const string AzureFunctionsUrl = "https://azure.microsoft.com/services/functions/";
    public const string DocsUrl = "https://aka.ms/func-cli";
    public const string GitHubUrl = "https://github.com/Azure/azure-functions-core-tools";
    public const string GitHubReleasesApiUrl = "https://api.github.com/repos/Azure/azure-functions-core-tools/releases";

    /// <summary>
    /// Env var users set to opt out of CLI telemetry. Matches the
    /// <c>FUNC_CLI_*</c> convention used elsewhere in v5.
    /// </summary>
    public const string TelemetryOptOutEnvVar = "FUNC_CLI_TELEMETRY_OPTOUT";

    /// <summary>
    /// Legacy opt-out env var inherited from Core Tools v4. Honored for
    /// back-compat so users who already set it on their machines are not
    /// silently re-opted in after upgrading.
    /// </summary>
    public const string LegacyTelemetryOptOutEnvVar = "FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT";

    /// <summary>
    /// Prefix for the CLI environment variables, including those that bind into <c>IConfiguration</c>.
    /// </summary>
    public const string EnvironmentVariablePrefix = "FUNC_CLI_";

    /// <summary>
    /// Environment variable that, when explicitly set to a non-empty value,
    /// overrides the default workload home directory. Read directly (not
    /// through <c>IConfiguration</c>) so the workload root cannot be
    /// redirected by host config, global config, or project
    /// <c>.func/config.json</c>.
    /// </summary>
    public const string WorkloadsHomeEnvironmentVariable = "FUNC_CLI_WORKLOADS_HOME";

    /// <summary>
    /// Environment variable that, when explicitly set to a non-empty value,
    /// overrides the default workload catalog source (a v3 NuGet
    /// <c>index.json</c> URL). Read directly (not through
    /// <c>IConfiguration</c>) so the source cannot be redirected by host
    /// config, global config, or project <c>.func/config.json</c>.
    /// </summary>
    public const string WorkloadsSourceEnvironmentVariable = "FUNC_CLI_WORKLOADS_SOURCE";

    /// <summary>
    /// Environment variable that, when set, allows prerelease workload
    /// versions across catalog resolution and installation flows.
    /// </summary>
    public const string WorkloadsPrereleaseEnvironmentVariable = "FUNC_CLI_WORKLOADS_PRERELEASE";

    /// <summary>
    /// Environment variable that overrides the CDN base URL used to fetch
    /// the remote profile registry.
    /// </summary>
    public const string ProfilesCdnBaseUrlEnvironmentVariable = "FUNC_CLI_PROFILES_CDN_BASE_URL";

    /// <summary>
    /// The telemetry instrumentation key, injected at build time via Telemetry.props.
    /// </summary>
    public static readonly string TelemetryInstrumentationKey =
        typeof(Constants).Assembly.GetCustomAttribute<TelemetryInstrumentationKeyAttribute>()?.Value
        ?? "00000000-0000-0000-0000-000000000000";
}

[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class TelemetryInstrumentationKeyAttribute(string value) : Attribute
{
    public string Value => value;
}
