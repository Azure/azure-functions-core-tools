// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Invokes one selected template while its owning command session remains alive.
/// </summary>
internal interface ISelectedTemplateInvoker
{
    public Task<TemplateInvocationResult> InvokeAsync(TemplateInvocationRequest request, CancellationToken cancellationToken);

    public Task<ReadOnlyMemory<byte>> ReadPackageFileAsync(string relativePath, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Selected-package source access is not available.");

    public Task<TemplateInvocationResult?> ValidateConfigurationAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Selected configuration validation is not available.");

    public Task<NativeTemplatePreparationResult> CaptureAsync(TemplateInvocationRequest request, bool expandFiles, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Selected-source capture is not available.");
}
