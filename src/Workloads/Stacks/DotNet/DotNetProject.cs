// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Base class for .NET Functions projects. Holds the common stack metadata.
/// </summary>
internal abstract class DotNetProject(WorkingDirectory workingDirectory) : FunctionsProject
{
    private const string DotNetStack = "dotnet";
    private const string DotNetStackDisplayName = ".NET";
    private const string DotNetIsolatedRuntime = "dotnet-isolated";

    private readonly WorkingDirectory _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => DotNetStack;

    public override string StackDisplayName => DotNetStackDisplayName;

    /// <summary>
    /// Defaults to C#. A source project overrides this from its project file
    /// extension; an output-only project (compiled binaries, no source) cannot
    /// recover the original language, so C# is the pragmatic default.
    /// </summary>
    public override string Language => "C#";

    public override bool SupportsExtensionBundles => false;

    public override FunctionsWorkerReference WorkerReference
        => FunctionsWorkerReference.FromWorkerInfo(DotNetStack, DotNetIsolatedRuntime, _workingDirectory.Info.FullName);
}
