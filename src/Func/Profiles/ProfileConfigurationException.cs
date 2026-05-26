// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Indicates that profile configuration could not be parsed or resolved.
/// </summary>
internal sealed class ProfileConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
