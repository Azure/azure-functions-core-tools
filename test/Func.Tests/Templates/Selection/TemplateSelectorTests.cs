// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Selection;
using static Azure.Functions.Cli.Tests.Templates.Selection.SelectionFixtures;

namespace Azure.Functions.Cli.Tests.Templates.Selection;

public sealed class TemplateSelectorTests
{
    private readonly TemplateSelector _selector = new(new());

    [Fact]
    public void Select_ExactIdentity_WinsOverAnotherTemplatesShortName()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("Other") with { ShortNames = ["Exact"], GroupIdentity = "other" }),
            new(Metadata("Exact") with { ShortNames = ["another"], GroupIdentity = "exact" }),
        ];

        var result = _selector.Select(descriptors, Request("Exact"));

        result.Status.Should().Be(TemplateSelectionStatus.Selected);
        result.Selected!.Template.Metadata.Identity.Should().Be("Exact");
    }

    [Fact]
    public void Select_IdentityIsCaseSensitiveButShortNameIsCaseInsensitive()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata())];

        var identity = _selector.Select(descriptors, Request("contoso.item"));
        var alias = _selector.Select(descriptors, Request("ITEM"));

        identity.Status.Should().Be(TemplateSelectionStatus.NotFound);
        alias.Status.Should().Be(TemplateSelectionStatus.Selected);
    }

    [Fact]
    public void Select_CaseDistinctOpaqueIdentities_SelectsTheExactOne()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata("A")), new(Metadata("a"))];

        var upper = _selector.Select(descriptors, Request("A"));
        var lower = _selector.Select(descriptors, Request("a"));

        upper.Selected!.Template.Metadata.Identity.Should().Be("A");
        lower.Selected!.Template.Metadata.Identity.Should().Be("a");
    }

    [Fact]
    public void Select_OpaqueIdentityWithOuterSpaces_IsNotTrimmed()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata(" Exact "))];

        var exact = _selector.Select(descriptors, Request(" Exact "));
        var trimmed = _selector.Select(descriptors, Request("Exact"));

        exact.Status.Should().Be(TemplateSelectionStatus.Selected);
        trimmed.Status.Should().Be(TemplateSelectionStatus.NotFound);
    }

    [Fact]
    public void Select_WrongTypeExactIdentity_DoesNotFallBackToAnItemAlias()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("Exact") with { Type = TemplateType.Project }),
            new(Metadata("Other") with { ShortNames = ["Exact"] }),
        ];

        var result = _selector.Select(descriptors, Request("Exact"));

        result.Status.Should().Be(TemplateSelectionStatus.WrongType);
        result.Selected.Should().BeNull();
        result.Candidates.Should().ContainSingle().Which.Metadata.Type.Should().Be(TemplateType.Project);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_SharedShortName_IsScopedToTheRequestedType(bool project)
    {
        var type = project ? TemplateType.Project : TemplateType.Item;
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("Item") with { GroupIdentity = "item-group" }),
            new(Metadata("Project") with { Type = TemplateType.Project, GroupIdentity = "project-group" }),
        ];

        var result = _selector.Select(descriptors, Request(type: type));

        result.Status.Should().Be(TemplateSelectionStatus.Selected);
        result.Selected!.Template.Metadata.Type.Should().Be(type);
    }

    [Fact]
    public void Select_EmptyCatalog_ReturnsExplicitNotFound()
    {
        var result = _selector.Select([], Request());

        result.Status.Should().Be(TemplateSelectionStatus.NotFound);
        result.Candidates.Should().BeEmpty();
        result.Selected.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_DuplicateIdentityIncludingUnmatchedDescriptors_RejectsTheEntireInput(bool conflict)
    {
        var first = new TemplateDescriptor(Metadata());
        var second = conflict
            ? new TemplateDescriptor(Metadata() with { Type = TemplateType.Project, Precedence = 99 })
            : first;

        Action action = () => _selector.Select([first, second], Request("unrelated"));

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*repeats template identities*");
    }

    [Fact]
    public void Select_UnrelatedGroups_CannotBeResolvedByPrecedenceLanguageOrArguments()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("A") with { GroupIdentity = "one", Precedence = 100, Language = "Other" }),
            new(Metadata("B") with { GroupIdentity = "two", Parameters = [Parameter()] }),
        ];
        var request = Request(tokens: ["--Message=value"]) with { Language = "JavaScript", ApplyPrecedence = true };

        var result = _selector.Select(descriptors, request);

        result.Status.Should().Be(TemplateSelectionStatus.AmbiguousGroup);
        result.Candidates.Select(candidate => candidate.Metadata.Identity).Should().Equal("A", "B");
        result.Selected.Should().BeNull();
    }

    [Fact]
    public void Select_UngroupedTemplates_FormSeparateSingletonGroups()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("A") with { GroupIdentity = null, Precedence = 100 }),
            new(Metadata("B") with { GroupIdentity = null }),
        ];

        var result = _selector.Select(descriptors, Request() with { ApplyPrecedence = true });

        result.Status.Should().Be(TemplateSelectionStatus.AmbiguousGroup);
    }

    [Fact]
    public void Select_GroupIdentityEqualToAnUngroupedIdentity_DoesNotMergeUnrelatedGroups()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("A") with { GroupIdentity = null }),
            new(Metadata("B") with { GroupIdentity = "A" }),
        ];

        var result = _selector.Select(descriptors, Request());

        result.Status.Should().Be(TemplateSelectionStatus.AmbiguousGroup);
    }

    [Fact]
    public void Select_GroupIdentitiesDifferOnlyByCase_RemainDistinct()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata("A") with { GroupIdentity = "Group" }), new(Metadata("B"))];

        var result = _selector.Select(descriptors, Request());

        result.Status.Should().Be(TemplateSelectionStatus.AmbiguousGroup);
    }

    [Fact]
    public void Select_LanguageFilter_IsOrdinalIgnoreCaseWithoutRuntimeAliases()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("A") with { Language = "JavaScript" }),
            new(Metadata("B") with { Language = "TypeScript" }),
        ];

        var matching = _selector.Select(descriptors, Request() with { Language = "JAVASCRIPT" });
        var alias = _selector.Select(descriptors, Request() with { Language = "js" });
        var padded = _selector.Select(descriptors, Request() with { Language = " JavaScript " });

        matching.Selected!.Template.Metadata.Identity.Should().Be("A");
        alias.Status.Should().Be(TemplateSelectionStatus.UnsatisfiedLanguage);
        padded.Status.Should().Be(TemplateSelectionStatus.UnsatisfiedLanguage);
    }

    [Fact]
    public void Select_MissingDescriptorLanguage_DoesNotInventAMatch()
    {
        var result = _selector.Select([new(Metadata() with { Language = null })], Request() with { Language = "JavaScript" });

        result.Status.Should().Be(TemplateSelectionStatus.UnsatisfiedLanguage);
        result.Selected.Should().BeNull();
    }

    [Fact]
    public void Select_ArgumentCompatibility_PrecedesExplicitPrecedenceNarrowing()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("Higher") with { Precedence = 100 }),
            new(Metadata("Lower") with { Precedence = 1, Parameters = [Parameter()] }),
        ];

        var result = _selector.Select(descriptors, Request(tokens: ["--Message=value"]) with { ApplyPrecedence = true });

        result.Status.Should().Be(TemplateSelectionStatus.Selected);
        result.Selected!.Template.Metadata.Identity.Should().Be("Lower");
        result.Selected.ValuesByCanonicalName["Message"].Should().Be("value");
        result.ParsedCandidates.Should().HaveCount(2);
        result.ParsedCandidates.Single(candidate => candidate.Template.Metadata.Identity == "Higher").Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void Select_InvalidTypeForHigherVariant_DoesNotHideAnArgumentCompatibleLowerVariant()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("Higher") with { Precedence = 100, Parameters = [Parameter(dataType: "int")] }),
            new(Metadata("Lower") with { Parameters = [Parameter()] }),
        ];

        var result = _selector.Select(descriptors, Request(tokens: ["--Message=text"]) with { ApplyPrecedence = true });

        result.Selected!.Template.Metadata.Identity.Should().Be("Lower");
    }

    [Fact]
    public void Select_MissingRequiredInput_DoesNotEliminateAnOtherwiseSelectableVariant()
    {
        ImmutableArray<TemplateDescriptor> descriptors =
        [
            new(Metadata("Higher") with { Precedence = 100, Parameters = [Parameter(required: true)] }),
            new(Metadata("Lower")),
        ];

        var result = _selector.Select(descriptors, Request() with { ApplyPrecedence = true });

        result.Status.Should().Be(TemplateSelectionStatus.Selected);
        result.Selected!.Template.Metadata.Identity.Should().Be("Higher");
        result.Selected.IsArgumentCompatible.Should().BeTrue();
        result.Selected.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Select_PrecedenceIsNotRequested_RemainsAmbiguous()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata("A") with { Precedence = 100 }), new(Metadata("B"))];

        var result = _selector.Select(descriptors, Request());

        result.Status.Should().Be(TemplateSelectionStatus.Ambiguous);
        result.Candidates.Should().HaveCount(2);
        result.Selected.Should().BeNull();
    }

    [Fact]
    public void Select_PrecedenceExplicitlyRequested_SelectsOnlyUniqueHighestVariant()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata("A")), new(Metadata("B") with { Precedence = 100 })];

        var result = _selector.Select(descriptors, Request() with { ApplyPrecedence = true });

        result.Selected!.Template.Metadata.Identity.Should().Be("B");
    }

    [Fact]
    public void Select_EqualHighestPrecedence_RemainsExplicitlyAmbiguous()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata("B") with { Precedence = 2 }), new(Metadata("A") with { Precedence = 2 }), new(Metadata("C"))];

        var result = _selector.Select(descriptors, Request() with { ApplyPrecedence = true });

        result.Status.Should().Be(TemplateSelectionStatus.Ambiguous);
        result.Candidates.Select(candidate => candidate.Metadata.Identity).Should().Equal("A", "B");
    }

    [Fact]
    public void Select_AllArgumentParsesInvalid_PreservesEveryCandidatesDiagnostics()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata("B")), new(Metadata("A"))];

        var result = _selector.Select(descriptors, Request(tokens: ["--unknown=value"]));

        result.Status.Should().Be(TemplateSelectionStatus.InvalidArguments);
        result.Candidates.Should().BeEmpty();
        result.ParsedCandidates.Select(candidate => candidate.Template.Metadata.Identity).Should().Equal("A", "B");
        result.Diagnostics.Should().Contain(message => message.StartsWith("Template 'A':", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(message => message.StartsWith("Template 'B':", StringComparison.Ordinal));
        result.Selected.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_AllDescriptorPermutations_ProduceStableSelectionOrAmbiguity(bool tie)
    {
        var a = new TemplateDescriptor(Metadata("A") with { Precedence = tie ? 1 : 0, Parameters = [Parameter()] });
        var b = new TemplateDescriptor(Metadata("B") with { Precedence = 1, Parameters = [Parameter()] });
        var c = new TemplateDescriptor(Metadata("C") with { Parameters = [Parameter()] });
        ImmutableArray<ImmutableArray<TemplateDescriptor>> permutations = [[a, b, c], [a, c, b], [b, a, c], [b, c, a], [c, a, b], [c, b, a]];

        foreach (var descriptors in permutations)
        {
            var result = _selector.Select(descriptors, Request(tokens: ["--Message=value"]) with { ApplyPrecedence = true });

            result.Status.Should().Be(tie ? TemplateSelectionStatus.Ambiguous : TemplateSelectionStatus.Selected);
            result.Candidates.Select(candidate => candidate.Metadata.Identity).Should().Equal(tie ? ["A", "B"] : ["B"]);
            result.ParsedCandidates.Select(candidate => candidate.Template.Metadata.Identity).Should().Equal("A", "B", "C");
            if (!tie)
            {
                result.Selected!.ValuesByCanonicalName["Message"].Should().Be("value");
                result.Selected.ExplicitParameters.Should().Contain("Message");
            }
        }
    }

    [Fact]
    public void Select_RepeatedRequests_DoNotMutateDescriptorsOrRetainPriorFilters()
    {
        ImmutableArray<TemplateDescriptor> descriptors = [new(Metadata("A")), new(Metadata("B") with { Language = "TypeScript" })];
        var request = Request() with { Language = "JavaScript" };

        var first = _selector.Select(descriptors, request);
        var other = _selector.Select(descriptors, request with { Language = "TypeScript" });
        var repeated = _selector.Select(descriptors, request);
        var unfiltered = _selector.Select(descriptors, Request());

        first.Selected!.Template.Metadata.Identity.Should().Be("A");
        other.Selected!.Template.Metadata.Identity.Should().Be("B");
        repeated.Selected!.Template.Should().BeSameAs(first.Selected.Template);
        repeated.Selected.ValuesByCanonicalName.Should().BeEquivalentTo(first.Selected.ValuesByCanonicalName);
        unfiltered.Status.Should().Be(TemplateSelectionStatus.Ambiguous);
        descriptors.Select(descriptor => descriptor.Metadata.Language).Should().Equal("JavaScript", "TypeScript");
    }

    [Fact]
    public void Select_OpaqueRequirementsAndPackageDetails_DoNotBecomeCompatibilityOrSelectionPolicy()
    {
        var declared = new TemplateDescriptor(
            Metadata("Declared") with { IsHidden = true, Package = new("Q:\\arbitrary\\archive.nupkg", "not-a-runtime-version") },
            """{"type":"func-project-capabilities","args":["Unfamiliar.Model","unfamiliar.model"]}""");
        var undeclared = new TemplateDescriptor(Metadata("Undeclared"));

        var result = _selector.Select([declared, undeclared], Request("Declared"));
        var absent = _selector.Select([undeclared], Request("Undeclared"));

        result.Status.Should().Be(TemplateSelectionStatus.Selected);
        result.Selected!.Template.Requirements.Should().BeSameAs(declared.Requirements);
        result.Selected.Template.Requirements.CapabilityIds.Should().Equal("Unfamiliar.Model", "unfamiliar.model");
        absent.Selected!.Template.Requirements.IsDeclared.Should().BeFalse();
    }

    [Fact]
    public void Select_InvalidArgumentsToTheApi_ThrowBeforeReturningAnOutcome()
    {
        var request = Request();
        Action dependency = () => new TemplateSelector(null!);
        Action nullRequest = () => _selector.Select([], null!);
        Action reference = () => _selector.Select([], request with { Reference = null! });
        Action blank = () => _selector.Select([], request with { Reference = " " });
        Action type = () => _selector.Select([], request with { Type = (TemplateType)99 });
        Action language = () => _selector.Select([], request with { Language = "" });
        Action descriptors = () => _selector.Select(default, request);
        Action descriptor = () => _selector.Select([null!], request);
        Action tokens = () => _selector.Select([], request with { Tokens = default });
        Action aliases = () => _selector.Select([], request with { ReservedAliases = default });

        dependency.Should().Throw<ArgumentNullException>().WithParameterName("argumentParser");
        nullRequest.Should().Throw<ArgumentNullException>().WithParameterName("request");
        reference.Should().Throw<ArgumentNullException>().WithParameterName("request");
        blank.Should().Throw<ArgumentException>().WithParameterName("request");
        type.Should().Throw<ArgumentOutOfRangeException>();
        language.Should().Throw<ArgumentException>();
        descriptors.Should().Throw<ArgumentException>().WithParameterName("descriptors");
        descriptor.Should().Throw<ArgumentException>().WithParameterName("descriptors");
        tokens.Should().Throw<ArgumentException>().WithParameterName("tokens");
        aliases.Should().Throw<ArgumentException>().WithParameterName("reservedAliases");
    }
}
