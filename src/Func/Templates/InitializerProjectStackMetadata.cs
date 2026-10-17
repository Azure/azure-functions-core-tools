// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Retains configuration-only metadata from older workloads without invoking their scaffolder.
/// </summary>
internal sealed class InitializerProjectStackMetadata(IProjectInitializer initializer) : IProjectStack
{
    private readonly IProjectInitializer _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));

    public string Stack => _initializer.Stack;

    public string DisplayName => _initializer.DisplayName;

    public IReadOnlyList<string> WorkerRuntimeAliases => _initializer.WorkerRuntimeAliases;

    public IReadOnlyList<string> SupportedLanguages => _initializer.SupportedLanguages;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases => _initializer.SupportedLanguageAliases;
}
