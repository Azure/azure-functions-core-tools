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
    public const string VersionCacheFileName = ".version-check";

    /// <summary>
    /// Directory name (under the user profile) the func CLI persists state in.
    /// </summary>
    public const string FuncHomeDirectoryName = ".azure-functions";

    /// <summary>
    /// Prefix for the CLI environment variables, including those that bind into <c>IConfiguration</c>.
    /// </summary>
    public const string EnvironmentVariablePrefix = "FUNC_CLI_";

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
