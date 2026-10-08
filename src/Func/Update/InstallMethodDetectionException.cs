// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Indicates that the current CLI installation method could not be determined safely.
/// </summary>
internal sealed class InstallMethodDetectionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
