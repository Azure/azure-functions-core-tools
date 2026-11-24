// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.DemoProject;

internal sealed class DemoProjectFactory : IFunctionsProjectFactory
{
    public Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ProjectCreationResults.Created(new DemoFunctionsProject(context.WorkingDirectory), "DemoProject"));
    }

    private sealed class DemoFunctionsProject(WorkingDirectory workingDirectory) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        private readonly FunctionsWorkerReference _workerReference = FunctionsWorkerReference.FromWorkerInfo(".NET", "dotnet-isolated", @"c:\test\worker.config.json", "1.0.0");

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override IProjectStack ProjectStack { get; } = new DemoStack();

        public override bool SupportsExtensionBundles => false;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }

    private sealed class DemoStack : IProjectStack
    {
        public StackDescriptor Descriptor { get; } = new("dotnet", "dotnet-isolated", ".NET");
        public IReadOnlyList<LanguageDescriptor> SupportedLanguages { get; } = [new("csharp", "C#", "c#")];
    }
}
