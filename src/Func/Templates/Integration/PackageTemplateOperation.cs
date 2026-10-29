// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;

namespace Azure.Functions.Cli.Templates.Integration;

/// <summary>
/// Exposes retained package output and delegates publication and cleanup to its prepared operation.
/// </summary>
internal sealed class PackageTemplateOperation(PreparedOperation operation, TemplatePackageContentLease package) : IAsyncDisposable
{
    private readonly PreparedOperation _operation = operation ?? throw new ArgumentNullException(nameof(operation));

    public string PackageSha256 { get; } = (package ?? throw new ArgumentNullException(nameof(package))).Sha256;

    public PreparedOperationCompletion Completion => _operation.Completion;

    public PreparedOperationPlan Preview() => _operation.Preview();

    public Task<PreparedOperationCompletion> CommitAsync(CancellationToken cancellationToken) => _operation.CommitAsync(cancellationToken);

    public ValueTask DisposeAsync() => _operation.DisposeAsync();
}
