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

    private sealed record DemoFunctionsProject(WorkingDirectory WorkingDirectory) : IFunctionsProject
    {
        public string StackName => "dotnet-isolated";

        public string StackDisplayName => ".NET";

        public bool SupportsExtensionBundles => false;

        public IFunctionsWorker Worker => new DotnetFunctionWorker();
    }

    private sealed class DotnetFunctionWorker : IFunctionsWorker
    {
        public FunctionsWorkerId Id => new("dotnet-isolated");

        public string WorkerRuntime => "dotnet-isolated";

        public string WorkerConfigPath => @"c:\\demo\\worker.config.json";

        public string Version => "1.0.0";
    }
}
