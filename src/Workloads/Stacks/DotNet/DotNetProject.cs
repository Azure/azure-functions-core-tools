// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Base class for .NET Functions projects. Holds the common stack metadata.
/// </summary>
internal abstract class DotNetProject(WorkingDirectory workingDirectory, DotNetProjectStack stack) : FunctionsProject
{
    private readonly WorkingDirectory _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override IProjectStack Stack { get; } = stack ?? throw new ArgumentNullException(nameof(stack));

    public override bool SupportsExtensionBundles => false;

    public override FunctionsWorkerReference WorkerReference
        => FunctionsWorkerReference.FromWorkerInfo(Stack.Descriptor.Identifier, Stack.Descriptor.WorkerRuntime, _workingDirectory.Info.FullName);
}
