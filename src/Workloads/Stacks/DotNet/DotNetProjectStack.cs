// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

internal sealed class DotNetProjectStack(StackDescriptor descriptor) : IProjectStack
{
    internal static LanguageDescriptor CSharp { get; } = new("csharp", "C#", "c#");
    internal static LanguageDescriptor FSharp { get; } = new("fsharp", "F#", "f#");

    public StackDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    public IReadOnlyList<LanguageDescriptor> SupportedLanguages { get; } = [CSharp, FSharp];
}
