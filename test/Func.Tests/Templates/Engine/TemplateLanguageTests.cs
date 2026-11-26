// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateLanguageTests
{
    [Theory]
    [InlineData("csharp", "C#", "c#")]
    [InlineData("fsharp", "F#", "f#")]
    [InlineData("javascript", "JavaScript", "js")]
    [InlineData("typescript", "TypeScript", "ts")]
    [InlineData("python", "Python", "py")]
    [InlineData("go", "Go", "golang")]
    public void ToTemplateTag_ReturnsTheTagShippedTemplatesUse_NotTheStackLanguageId(
        string id, string expectedTag, string alias)
    {
        LanguageDescriptor language = new(id, expectedTag, alias);

        TemplateLanguage.ToTemplateTag(language).Should().Be(expectedTag);
    }
}
