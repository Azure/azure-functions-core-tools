// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateResolutionResultTests
{
    [Fact]
    public void Candidates_RepresentFailureSelectionAndAmbiguityByCount()
    {
        TemplateCatalogEntry entry = new(
            "Func.Tests.Template",
            "Template",
            null,
            TemplateType.Item,
            ["template"],
            null,
            null,
            0,
            null,
            false,
            [],
            null);

        TemplateResolutionResult failure = new([], [new("not-found", "No template matched.")]);
        TemplateResolutionResult selected = new([entry], []);
        TemplateResolutionResult ambiguous = new([entry, entry with { }], []);

        failure.Candidates.Should().BeEmpty();
        selected.Candidates.Should().ContainSingle().Which.Should().BeSameAs(entry);
        ambiguous.Candidates.Should().HaveCountGreaterThan(1);
    }
}
