// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

internal class InvalidInstallationMetadataException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class InstallationMetadataTooLargeException(string message, Exception innerException)
    : InvalidInstallationMetadataException(message, innerException);

internal sealed class UnsupportedInstallationMetadataSchemaException(string message)
    : InvalidInstallationMetadataException(message);
