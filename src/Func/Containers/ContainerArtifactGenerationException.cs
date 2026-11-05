// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Containers;

internal sealed class ContainerArtifactGenerationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
