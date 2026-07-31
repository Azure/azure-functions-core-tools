// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Relocates the templating engine's settings tree under the func CLI home so
/// the engine's package store and template cache never touch the user's
/// <c>dotnet new</c> state. Isolation hinges on overriding the engine's
/// <em>global</em> settings dir (not just the host id): host-id alone leaves
/// <c>~/.templateengine</c> shared. Layout mirrors the engine's own
/// convention:
/// <list type="bullet">
///   <item><description>global — <c>&lt;func-home&gt;/template-engine</c></description></item>
///   <item><description>host — <c>&lt;func-home&gt;/template-engine/func</c></description></item>
///   <item><description>host version — <c>&lt;func-home&gt;/template-engine/func/&lt;cli-version&gt;</c></description></item>
/// </list>
/// </summary>
internal sealed class FuncTemplateEnginePaths : IPathInfo
{
    /// <summary>
    /// Host identifier the engine matches template host files (<c>func.host.json</c>) and settings dirs against.
    /// </summary>
    internal const string HostIdentifier = "func";

    private const string TemplateEngineDirectoryName = "template-engine";

    /// <summary>
    /// Resolves the hive under the func CLI home (honoring
    /// <see cref="Constants.FuncHomeEnvironmentVariable"/>) scoped to the
    /// running CLI version.
    /// </summary>
    public FuncTemplateEnginePaths(IFuncTemplateEngineVersion version)
        : this(
            FuncHomeResolver.Resolve(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            (version ?? throw new ArgumentNullException(nameof(version))).Version)
    {
    }

    /// <summary>
    /// Test seam: supplies the func home, user profile, and CLI version
    /// directly so tests can point the hive at a redirected temp home without
    /// mutating process-global environment variables.
    /// </summary>
    internal FuncTemplateEnginePaths(string funcHome, string userProfileDir, string cliVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(funcHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliVersion);

        UserProfileDir = Path.GetFullPath(userProfileDir);
        GlobalSettingsDir = Path.Combine(Path.GetFullPath(funcHome), TemplateEngineDirectoryName);
        HostSettingsDir = Path.Combine(GlobalSettingsDir, HostIdentifier);
        HostVersionSettingsDir = Path.Combine(HostSettingsDir, cliVersion);
    }

    /// <inheritdoc />
    public string UserProfileDir { get; }

    /// <inheritdoc />
    public string GlobalSettingsDir { get; }

    /// <inheritdoc />
    public string HostSettingsDir { get; }

    /// <inheritdoc />
    public string HostVersionSettingsDir { get; }
}
