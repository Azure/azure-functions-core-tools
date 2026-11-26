// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Python;

internal sealed class PythonProjectStack(StackDescriptor descriptor) : IProjectStack
{
    public StackDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    public IReadOnlyList<LanguageDescriptor> SupportedLanguages { get; } = [new("python", "Python", "py")];
}
