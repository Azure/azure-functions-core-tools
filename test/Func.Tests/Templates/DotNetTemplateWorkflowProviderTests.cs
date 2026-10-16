// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Workloads.DotNet;
using Shared = Azure.Functions.Cli.Templates.DotNet.Shared;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetTemplateWorkflowProviderTests
{
    [Fact]
    public void HostHelperCopy_IsCliOwnedAndNotAPhysicalWorkloadServiceOrRuntimeReference()
    {
        typeof(DotNetTemplateWorkflowProvider).Assembly.Should().BeSameAs(typeof(Shared.DotNetTemplateDependencyExecutor).Assembly);
        typeof(Shared.DotNetTemplateDependencyExecutor).Assembly.Should().NotBeSameAs(typeof(DotNetWorkload).Assembly);
        typeof(DotNetTemplateWorkflowProvider).Assembly.GetReferencedAssemblies()
            .Should().NotContain(reference => reference.Name == "Azure.Functions.Cli.Workloads.DotNet");
    }
}
