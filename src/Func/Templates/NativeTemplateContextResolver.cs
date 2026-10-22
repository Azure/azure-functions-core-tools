// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Reuses command resolution with installed stack metadata for explicitly composed native invocations.
/// </summary>
internal sealed class NativeTemplateContextResolver(
    IFileSystem fileSystem,
    IFunctionsProjectResolver projects,
    IProfileResolver profiles,
    IOptionsMonitor<StackOptions> stackOptions,
    InstalledProjectStackCatalog stackCatalog,
    IHostJsonBundleSectionReader hostReader,
    IExtensionBundleResolver bundles,
    TemplateEngineBundleContextReader bundleContextReader,
    bool enableBundleBindingConstraints = false,
    NativeTemplateStackCompatibility? stackCompatibility = null) : INewExecutionContextResolver
{
    private readonly InstalledProjectStackCatalog _stackCatalog = stackCatalog ?? throw new ArgumentNullException(nameof(stackCatalog));
    private readonly NewExecutionContextResolver _resolver = new(fileSystem, projects, profiles, stackOptions, [], hostReader, bundles,
        bundleContextReader ?? throw new ArgumentNullException(nameof(bundleContextReader)), enableBundleBindingConstraints);

    public Task<NewExecutionContextResult> ResolveAsync(NewExecutionRequest request, bool canPrompt, CancellationToken cancellationToken)
        => _resolver.ResolveNativeAsync(request, canPrompt, _stackCatalog, cancellationToken, stackCompatibility);
}
