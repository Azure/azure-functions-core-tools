// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Represents a resolved Azure Functions project.
/// </summary>
public interface IFunctionsProject
{
    public WorkingDirectory WorkingDirectory { get; }

    public string StackName { get; }

    public string StackDisplayName { get; }

    public bool SupportsExtensionBundles { get; }

    public IFunctionsWorker Worker { get; }
}
