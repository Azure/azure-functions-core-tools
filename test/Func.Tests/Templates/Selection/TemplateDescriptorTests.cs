// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Selection;
using static Azure.Functions.Cli.Tests.Templates.Selection.SelectionFixtures;

namespace Azure.Functions.Cli.Tests.Templates.Selection;

public sealed class TemplateDescriptorTests
{
    [Fact]
    public void Constructor_MinimalMetadata_RetainsIdentityWithoutInventingRequirements()
    {
        var descriptor = new TemplateDescriptor(Metadata() with { ShortNames = [], GroupIdentity = null, Language = null });

        descriptor.Metadata.Identity.Should().Be("Contoso.Item");
        descriptor.Metadata.Parameters.Should().BeEmpty();
        descriptor.Requirements.IsDeclared.Should().BeFalse();
        descriptor.Requirements.CapabilityIds.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_OptionalMetadata_PreservesExactAuthoredValues()
    {
        var metadata = Metadata(" Identity ") with
        {
            Description = "",
            GroupIdentity = " Group ",
            Language = " Language ",
            Package = new("Opaque.Package", "version-label"),
            IsHidden = true,
            Precedence = -7,
            Parameters = [Parameter("Number", "int", "+001", required: true) with { AlwaysShow = true }],
        };

        var descriptor = new TemplateDescriptor(metadata, """{"type":"func-project-capabilities","args":["Future.Model","future.model"]}""");

        descriptor.Metadata.Should().BeEquivalentTo(metadata);
        descriptor.Metadata.Parameters[0].DefaultValue.Should().Be("+001");
        descriptor.Requirements.CapabilityIds.Should().Equal("Future.Model", "future.model");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Constructor_MissingRequiredText_RejectsMetadata(string? value)
    {
        Action identity = () => new TemplateDescriptor(Metadata() with { Identity = value! });
        Action name = () => new TemplateDescriptor(Metadata() with { Name = value! });
        Action canonical = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter() with { CanonicalName = value! }] });
        Action dataType = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter() with { DataType = value! }] });

        identity.Should().Throw<InvalidTemplateMetadataException>();
        name.Should().Throw<InvalidTemplateMetadataException>();
        canonical.Should().Throw<InvalidTemplateMetadataException>();
        dataType.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_BlankOptionalIdentityOrLanguage_RejectsRatherThanTreatingItAsAbsent(string value)
    {
        Action group = () => new TemplateDescriptor(Metadata() with { GroupIdentity = value });
        Action language = () => new TemplateDescriptor(Metadata() with { Language = value });
        Action package = () => new TemplateDescriptor(Metadata() with { Package = new(value, null) });
        Action version = () => new TemplateDescriptor(Metadata() with { Package = new("Package", value) });

        group.Should().Throw<InvalidTemplateMetadataException>();
        language.Should().Throw<InvalidTemplateMetadataException>();
        package.Should().Throw<InvalidTemplateMetadataException>();
        version.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Constructor_UnsupportedTemplateType_RejectsIt()
    {
        Action action = () => new TemplateDescriptor(Metadata() with { Type = (TemplateType)99 });

        action.Should().Throw<NotSupportedException>().WithMessage("*template type*");
    }

    [Fact]
    public void Constructor_MetadataDiagnostic_DoesNotTurnAnInvalidCandidateIntoAUsableSubset()
    {
        Action action = () => new TemplateDescriptor(Metadata() with { MetadataDiagnostic = "Duplicate interpreted property." });

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*Duplicate interpreted property*");
    }

    [Fact]
    public void Constructor_NullMetadata_ThrowsArgumentNull()
    {
        Action action = () => new TemplateDescriptor(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("metadata");
    }

    [Fact]
    public void TryConvertValue_NullArguments_ThrowArgumentNull()
    {
        Action parameter = () => TemplateDescriptor.TryConvertValue(null!, "value", out _);
        Action value = () => TemplateDescriptor.TryConvertValue(Parameter(), null!, out _);

        parameter.Should().Throw<ArgumentNullException>().WithParameterName("parameter");
        value.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [Fact]
    public void Constructor_NullCollectionsOrEntries_RejectsMetadata()
    {
        Action aliases = () => new TemplateDescriptor(Metadata() with { ShortNames = null! });
        Action parameters = () => new TemplateDescriptor(Metadata() with { Parameters = null! });
        Action parameter = () => new TemplateDescriptor(Metadata() with { Parameters = [null!] });
        Action choices = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter() with { Choices = null! }] });
        Action choice = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter(dataType: "choice") with { Choices = [null!] }] });

        aliases.Should().Throw<InvalidTemplateMetadataException>();
        parameters.Should().Throw<InvalidTemplateMetadataException>();
        parameter.Should().Throw<InvalidTemplateMetadataException>();
        choices.Should().Throw<InvalidTemplateMetadataException>();
        choice.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_BlankShortName_RejectsIt(string? alias)
    {
        Action action = () => new TemplateDescriptor(Metadata() with { ShortNames = [alias!] });

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Constructor_CaseEquivalentShortNames_RejectsDuplicateSelectionAliases()
    {
        Action action = () => new TemplateDescriptor(Metadata() with { ShortNames = ["item", "ITEM"] });

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*repeats short name*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-value")]
    [InlineData("a b")]
    [InlineData("a:b")]
    [InlineData("a=b")]
    [InlineData("a\u0000b")]
    [InlineData("a\nb")]
    public void Constructor_InvalidLongAlias_RejectsItWithoutNormalizing(string? alias)
    {
        Action action = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter() with { LongName = alias! }] });

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*invalid alias*");
    }

    [Theory]
    [InlineData("-v")]
    [InlineData(" ")]
    [InlineData("a:b")]
    [InlineData("a=b")]
    [InlineData("a\u0000b")]
    public void Constructor_InvalidShortAlias_RejectsIt(string alias)
    {
        Action action = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter() with { ShortName = alias }] });

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_MissingOrEmptyShortAlias_SuppressesIt(string? alias)
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter() with { ShortName = alias }] });

        descriptor.Metadata.Parameters[0].ShortName.Should().BeNull();
    }

    [Theory]
    [InlineData("canonical")]
    [InlineData("long")]
    [InlineData("short")]
    public void Constructor_DuplicateParameterIdentityOrAlias_RejectsTheSchema(string duplicate)
    {
        var first = Parameter("First") with { ShortName = "f" };
        var second = Parameter("Second") with { ShortName = "s" };
        second = duplicate switch
        {
            "canonical" => second with { CanonicalName = first.CanonicalName },
            "long" => second with { LongName = first.LongName },
            _ => second with { ShortName = first.ShortName },
        };

        Action action = () => new TemplateDescriptor(Metadata() with { Parameters = [first, second] });

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*repeats*");
    }

    [Fact]
    public void Constructor_CaseDistinctCanonicalNamesAndOptionAliases_PreservesBoth()
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter("Value"), Parameter("value")] });

        descriptor.Metadata.Parameters.Select(parameter => parameter.CanonicalName).Should().Equal("Value", "value");
    }

    [Theory]
    [InlineData("date")]
    [InlineData("hex")]
    [InlineData("STRING")]
    [InlineData("bool ")]
    public void Constructor_UnsupportedDataType_DoesNotDefaultToString(string type)
    {
        Action action = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter(dataType: type)] });

        action.Should().Throw<NotSupportedException>().WithMessage("*unsupported data type*");
    }

    [Fact]
    public void Constructor_ChoiceSchemaMismatch_RejectsIt()
    {
        Action empty = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter(dataType: "choice")] });
        Action unexpected = () => new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter() with { Choices = [new("Hot", null, null)] }],
        });
        Action duplicate = () => new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter(dataType: "choice") with { Choices = [new("Hot", null, null), new("HOT", null, null)] }],
        });
        Action blank = () => new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter(dataType: "choice") with { Choices = [new("", null, null)] }],
        });

        empty.Should().Throw<InvalidTemplateMetadataException>();
        unexpected.Should().Throw<InvalidTemplateMetadataException>();
        duplicate.Should().Throw<InvalidTemplateMetadataException>();
        blank.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("bool", "")]
    [InlineData("bool", "yes")]
    [InlineData("int", "1.2")]
    [InlineData("int", " 1")]
    [InlineData("int", "2147483648")]
    [InlineData("float", "NaN")]
    [InlineData("float", "Infinity")]
    [InlineData("float", "1e999")]
    [InlineData("float", "1,5")]
    public void Constructor_InvalidAuthoredDefault_RejectsItInsteadOfReplacingIt(string type, string value)
    {
        Action action = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter(dataType: type, defaultValue: value)] });

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*authored default*");
    }

    [Fact]
    public void Constructor_DefaultOutsideChoices_RejectsIt()
    {
        Action action = () => new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter(dataType: "choice", defaultValue: "Cold") with { Choices = [new("Hot", null, null)] }],
        });

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*authored default*");
    }

    [Fact]
    public void Constructor_HiddenRequiredParameterWithoutDefault_IsAnAuthoringFailure()
    {
        Action action = () => new TemplateDescriptor(Metadata() with { Parameters = [Parameter(required: true) with { IsHidden = true }] });

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*required and hidden*");
    }

    [Fact]
    public void Constructor_HiddenRequiredStringWithEmptyDefault_PreservesTheDefault()
    {
        var descriptor = new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter(defaultValue: "", required: true) with { IsHidden = true }],
        });

        descriptor.Metadata.Parameters[0].DefaultValue.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"type":"func-project-capabilities"}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a"],"a\u0072gs":["b"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[false]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[" a"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a/b"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a","a"]}""")]
    public void Constructor_MalformedCapabilityDeclaration_RejectsTheRetainedValidationFailure(string declaration)
    {
        Action action = () => new TemplateDescriptor(Metadata(), declaration);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*project capability requirements*");
    }

    [Theory]
    [InlineData("""{"type":"FUNC-PROJECT-CAPABILITIES","args":["a"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a"],"unknown":true}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a"],"schemaVersion":2}""")]
    public void Constructor_UnsupportedCapabilitySyntax_DoesNotIgnoreIt(string declaration)
    {
        Action action = () => new TemplateDescriptor(Metadata(), declaration);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*project capability requirements*");
    }

    [Fact]
    public void Constructor_CallerMutatesAllInputCollections_SnapshotRemainsUnchanged()
    {
        List<string> shortNames = ["item"];
        List<TemplateParameterChoice> choices = [new("Hot", "Hot label", "Description")];
        List<TemplateParameterDefinition> parameters = [Parameter(dataType: "choice") with { Choices = choices }];
        var descriptor = new TemplateDescriptor(Metadata() with { ShortNames = shortNames, Parameters = parameters });

        shortNames.Clear();
        choices.Clear();
        parameters.Clear();

        descriptor.Metadata.ShortNames.Should().Equal("item");
        descriptor.Metadata.Parameters.Should().ContainSingle().Which.Choices.Should().ContainSingle().Which.Value.Should().Be("Hot");
    }

    [Fact]
    public void Constructor_CapturedCollections_RejectMutationThroughMutableInterfaces()
    {
        var descriptor = new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter(dataType: "choice") with { Choices = [new("Hot", null, null)] }],
        });

        Action aliases = () => ((IList<string>)descriptor.Metadata.ShortNames).Clear();
        Action parameters = () => ((IList<TemplateParameterDefinition>)descriptor.Metadata.Parameters).Clear();
        Action choices = () => ((IList<TemplateParameterChoice>)descriptor.Metadata.Parameters[0].Choices).Clear();

        aliases.Should().Throw<NotSupportedException>();
        parameters.Should().Throw<NotSupportedException>();
        choices.Should().Throw<NotSupportedException>();
    }
}
