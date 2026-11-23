// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Reports a template package validation or lifecycle failure.
/// </summary>
internal sealed class TemplatePackageException(string message, Exception? innerException = null) : Exception(message, innerException);
