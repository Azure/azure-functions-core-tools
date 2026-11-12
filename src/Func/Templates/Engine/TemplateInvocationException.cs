// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplateInvocationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
