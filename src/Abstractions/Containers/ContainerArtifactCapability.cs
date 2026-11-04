// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Containers;

/// <summary>
/// Identifies the container artifact set requested by the CLI.
/// </summary>
public enum ContainerArtifactCapability
{
    Docker,
    FlexByoc,
}
