// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes an installed stack without project generation behavior.
/// This public contract is implemented by independently shipped workload assemblies.
/// </summary>
public interface IProjectStack
{
    public StackDescriptor Descriptor { get; }

    public IReadOnlyList<LanguageDescriptor> SupportedLanguages { get; }
}
