// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Thrown when project extension bundle configuration cannot be read or parsed.
/// </summary>
internal sealed class ExtensionBundleConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
