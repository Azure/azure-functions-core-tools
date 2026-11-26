// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplateFiltering;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateResolutionResultTests
{
    [Fact]
    public void Constructor_NullTemplates_Throws()
    {
        Action act = () => _ = new TemplateResolutionResult((IReadOnlyList<FuncTemplateInfo>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("templates");
    }

    [Fact]
    public void Constructor_NullCandidates_Throws()
    {
        Action act = () => _ = new TemplateResolutionResult(
            (IReadOnlyList<(FuncTemplateInfo, ITemplateMatchInfo)>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("candidates");
    }

    [Fact]
    public void Constructor_Candidates_KeepsOrderAndAssociatesEachCandidateWithItsOwnMatch()
    {
        ITemplateInfo shared = Substitute.For<ITemplateInfo>();
        shared.Identity.Returns("Same.Identity");
        FuncTemplateInfo first = new(shared, FuncTemplateHostData.Empty);
        FuncTemplateInfo second = new(shared, FuncTemplateHostData.Empty);
        ITemplateMatchInfo firstMatch = Substitute.For<ITemplateMatchInfo>();
        ITemplateMatchInfo secondMatch = Substitute.For<ITemplateMatchInfo>();

        TemplateResolutionResult result = new([(second, secondMatch), (first, firstMatch)]);

        result.Templates.Should().Equal(second, first);
        result.Matches.Should().HaveCount(2);
        result.Matches[first].Should().BeSameAs(firstMatch);
        result.Matches[second].Should().BeSameAs(secondMatch);
    }
}
