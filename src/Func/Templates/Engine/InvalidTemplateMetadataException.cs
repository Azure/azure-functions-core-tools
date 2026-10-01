// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Thrown when a template's func host metadata is malformed or gives its parameters unusable aliases.
/// </summary>
internal sealed class InvalidTemplateMetadataException(string message, Exception? innerException = null)
    : Exception(message, innerException);
