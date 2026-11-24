// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Tests;

internal sealed class TestProjectStack(string id, string displayName, string? workerRuntime = null, params LanguageDescriptor[] languages) : IProjectStack
{
    public StackDescriptor Descriptor { get; } = new(id, workerRuntime ?? id, displayName);
    public IReadOnlyList<LanguageDescriptor> SupportedLanguages { get; } = languages;
}
