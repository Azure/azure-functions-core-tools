// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Contributes quickstart scaffolding guidance for its stack's languages with catalog templates.
/// </summary>
public interface IQuickstartProvider
{
    public IProjectStack Stack { get; }

    public IReadOnlyList<string> GetNextSteps(LanguageDescriptor language);
}
