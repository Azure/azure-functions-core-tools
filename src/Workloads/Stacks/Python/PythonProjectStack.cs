// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Python;

internal sealed class PythonProjectStack : IProjectStack
{
    public string Stack => "python";

    public string DisplayName => "Python";

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        { "Python", ["py"] }
    };
}
