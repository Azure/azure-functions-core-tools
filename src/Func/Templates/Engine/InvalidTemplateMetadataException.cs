// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Thrown when func host metadata or raw constraint declarations are malformed or unusable.
/// </summary>
internal sealed class InvalidTemplateMetadataException(string message, Exception? innerException = null)
    : Exception(message, innerException);
