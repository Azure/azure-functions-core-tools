// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Invokes one selected template while its owning command session remains alive.
/// </summary>
internal interface ISelectedTemplateInvoker
{
    public Task<TemplateInvocationResult> InvokeAsync(TemplateInvocationRequest request, CancellationToken cancellationToken);
}
