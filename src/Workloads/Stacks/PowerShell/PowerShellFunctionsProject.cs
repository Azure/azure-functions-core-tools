// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Workloads.PowerShell;

/// <summary>
/// PowerShell Functions project. PowerShell is an interpreted language with no
/// compile step, so <see cref="PrepareForHostRunAsync"/> is a no-op. The host
/// worker handles module resolution at runtime via <c>requirements.psd1</c> and
/// the <c>Modules</c> folder.
/// </summary>
internal sealed class PowerShellFunctionsProject : FunctionsProject
{
    private readonly WorkingDirectory _workingDirectory;
    private readonly FunctionsWorkerReference _workerReference;

    public PowerShellFunctionsProject(WorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _workerReference = FunctionsWorkerReference.FromWorkload("powershell");
    }

    public override WorkingDirectory WorkingDirectory => _workingDirectory;

    public override string StackName => "powershell";

    public override string StackDisplayName => "PowerShell";

    public override bool SupportsExtensionBundles => true;

    public override FunctionsWorkerReference WorkerReference => _workerReference;
}
