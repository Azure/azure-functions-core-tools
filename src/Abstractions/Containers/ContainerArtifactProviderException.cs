// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Containers;

/// <summary>
/// Represents a workload-specific container artifact validation or generation failure.
/// </summary>
public sealed class ContainerArtifactProviderException(string message, Exception? innerException = null) : Exception(message, innerException);
