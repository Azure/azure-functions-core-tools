// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Provides the file-system paths for the dotnet template hive used by
/// <see cref="DotNetProjectInitializer"/>.
/// </summary>
internal interface ITemplateHivePathProvider
{
    /// <summary>
    /// Root directory of the custom template hive.
    /// </summary>
    public string HivePath { get; }

    /// <summary>
    /// Path to the timestamp file that records when templates were last installed.
    /// </summary>
    public string TimestampPath { get; }

    /// <summary>
    /// The CLI hive shared with Microsoft.TemplateEngine.
    /// </summary>
    public string TemplateEngineHivePath { get; }

    /// <summary>
    /// The template package archives delivered with this workload.
    /// </summary>
    public string TemplatePackageDirectory { get; }
}
