// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// A .NET Functions project detected from build output (has host.json, worker.config.json, and an .exe).
/// Already compiled; no build step needed before host startup.
/// </summary>
internal sealed class DotNetOutputProject(WorkingDirectory workingDirectory, IFunctionsWorker worker) : FunctionsProject
{
    private readonly WorkingDirectory _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
    private readonly IFunctionsWorker _worker = worker ?? throw new ArgumentNullException(nameof(worker));

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => "dotnet";

    public override string StackDisplayName => ".NET";

    public override bool SupportsExtensionBundles => false;

    public override IFunctionsWorker Worker => _worker;
}
