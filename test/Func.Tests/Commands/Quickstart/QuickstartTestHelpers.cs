// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Quickstart;

internal static class QuickstartTestHelpers
{
    public static IQuickstartProvider CreateProvider(
        string stack = "python",
        string displayName = "Python",
        params string[] manifestLanguages)
    {
        if (manifestLanguages.Length == 0)
        {
            manifestLanguages = ["Python"];
        }

        IQuickstartProvider provider = Substitute.For<IQuickstartProvider>();
        provider.Stack.Returns(stack);
        provider.DisplayName.Returns(displayName);
        provider.ManifestLanguages.Returns(manifestLanguages.ToList().AsReadOnly());
        return provider;
    }

    public static QuickstartEntry CreateEntry(
        string id = "http-py",
        string displayName = "HTTP Trigger",
        string language = "Python",
        string? shortDescription = "A Python HTTP trigger",
        string resource = "http",
        string? iac = "bicep") =>
        new(id, displayName, language, resource, iac,
            "https://github.com/Azure-Samples/test-repo", ".", "refs/tags/v1.0.0",
            shortDescription, null, null, 100);
}
