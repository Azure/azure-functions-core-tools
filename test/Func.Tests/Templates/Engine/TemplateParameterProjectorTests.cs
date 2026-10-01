// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateParameterProjectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-parameter-projector-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }

    [Fact]
    public void Project_WithoutHostMetadata_UsesCanonicalNameAsLongName()
    {
        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
            [Parameter("Level")],
            FuncHostTemplateMetadata.Empty);

        TemplateParameterDefinition definition = definitions.Should().ContainSingle().Subject;
        definition.CanonicalName.Should().Be("Level");
        definition.LongName.Should().Be("Level");
        definition.ShortName.Should().BeNull();
        definition.IsHidden.Should().BeFalse();
        definition.AlwaysShow.Should().BeFalse();
    }

    [Fact]
    public void Project_HostMetadata_AppliesAliases()
    {
        FuncHostTemplateMetadata metadata = Metadata(("Level", Symbol(longName: "log-level", shortName: "l")));

        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project([Parameter("Level")], metadata);

        TemplateParameterDefinition definition = definitions.Should().ContainSingle().Subject;
        definition.CanonicalName.Should().Be("Level");
        definition.LongName.Should().Be("log-level");
        definition.ShortName.Should().Be("l");
    }

    [Fact]
    public void Project_HostMetadataWithoutLongName_UsesCanonicalName()
    {
        FuncHostTemplateMetadata metadata = Metadata(("Level", Symbol(shortName: "l")));

        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project([Parameter("Level")], metadata);

        definitions.Should().ContainSingle().Which.LongName.Should().Be("Level");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Project_HostMetadata_AppliesEachVisibilityFlagIndependently(bool isHidden, bool alwaysShow)
    {
        FuncHostTemplateMetadata metadata = Metadata(("Level", Symbol(isHidden: isHidden, alwaysShow: alwaysShow)));

        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project([Parameter("Level")], metadata);

        TemplateParameterDefinition definition = definitions.Should().ContainSingle().Subject;
        definition.IsHidden.Should().Be(isHidden);
        definition.AlwaysShow.Should().Be(alwaysShow);
    }

    [Fact]
    public void Project_DisabledParameter_IsHidden()
    {
        FuncHostTemplateMetadata metadata = Metadata(("Level", Symbol(longName: "log-level")));

        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
            [Parameter("Level", precedence: PrecedenceDefinition.Disabled)],
            metadata);

        TemplateParameterDefinition definition = definitions.Should().ContainSingle().Subject;
        definition.IsHidden.Should().BeTrue();
        definition.LongName.Should().Be("log-level");
    }

    [Fact]
    public void Project_ConditionallyDisabledParameter_IsNotHidden()
    {
        ITemplateParameter parameter = Parameter("Level");
        parameter.Precedence.Returns(new TemplateParameterPrecedence(PrecedenceDefinition.ConditionalyDisabled, isEnabledCondition: "Other == true"));

        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project([parameter], FuncHostTemplateMetadata.Empty);

        definitions.Should().ContainSingle().Which.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Project_NonParameterSymbol_IsExcluded()
    {
        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
            [Parameter("FuncStack", type: "bind"), Parameter("Level")],
            FuncHostTemplateMetadata.Empty);

        definitions.Should().ContainSingle().Which.CanonicalName.Should().Be("Level");
    }

    [Fact]
    public void Project_NameParameter_IsExcluded()
    {
        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
            [Parameter("name", precedence: PrecedenceDefinition.Required), Parameter("Name")],
            FuncHostTemplateMetadata.Empty);

        definitions.Should().ContainSingle().Which.CanonicalName.Should().Be("Name");
    }

    [Fact]
    public void Project_ImplicitTagParameter_IsExcluded()
    {
        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
            [Parameter("language", dataType: "choice", precedence: PrecedenceDefinition.Implicit), Parameter("Level")],
            FuncHostTemplateMetadata.Empty);

        definitions.Should().ContainSingle().Which.CanonicalName.Should().Be("Level");
    }

    [Theory]
    [InlineData(PrecedenceDefinition.Required, true)]
    [InlineData(PrecedenceDefinition.Optional, false)]
    public void Project_Precedence_SetsIsRequired(PrecedenceDefinition precedence, bool expected)
    {
        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
            [Parameter("Level", precedence: precedence)],
            FuncHostTemplateMetadata.Empty);

        definitions.Should().ContainSingle().Which.IsRequired.Should().Be(expected);
    }

    [Fact]
    public void Project_ConditionallyRequiredParameter_IsNotRequired()
    {
        ITemplateParameter parameter = Parameter("Level");
        parameter.Precedence.Returns(new TemplateParameterPrecedence(PrecedenceDefinition.ConditionalyRequired, isRequiredCondition: "Other == true"));

        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project([parameter], FuncHostTemplateMetadata.Empty);

        definitions.Should().ContainSingle().Which.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void Project_ChoiceParameter_KeepsChoicesInOrder()
    {
        ITemplateParameter parameter = Parameter("Level", dataType: "choice", defaultValue: "Low");
        parameter.Choices.Returns(new Dictionary<string, ParameterChoice>
        {
            ["Low"] = new(displayName: "Low level", description: string.Empty),
            ["High"] = new(displayName: string.Empty, description: "Most output"),
        });

        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project([parameter], FuncHostTemplateMetadata.Empty);

        TemplateParameterDefinition definition = definitions.Should().ContainSingle().Subject;
        definition.DataType.Should().Be("choice");
        definition.DefaultValue.Should().Be("Low");
        definition.Choices.Should().Equal(
            new TemplateParameterChoice("Low", "Low level", Description: null),
            new TemplateParameterChoice("High", DisplayName: null, "Most output"));
    }

    [Fact]
    public void Project_EmptyDescription_IsNull()
    {
        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
            [Parameter("Level", description: string.Empty)],
            FuncHostTemplateMetadata.Empty);

        definitions.Should().ContainSingle().Which.Description.Should().BeNull();
    }

    [Fact]
    public void Project_MissingDataType_IsString()
    {
        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
            [Parameter("Level", dataType: null)],
            FuncHostTemplateMetadata.Empty);

        definitions.Should().ContainSingle().Which.DataType.Should().Be("string");
    }

    [Theory]
    [InlineData("level")]
    [InlineData("name")]
    [InlineData("language")]
    [InlineData("FuncStack")]
    public void Project_HostMetadataForSymbolUsersCannotSet_Throws(string symbol)
    {
        FuncHostTemplateMetadata metadata = Metadata((symbol, Symbol(longName: "log-level")));

        Action act = () => TemplateParameterProjector.Project(
            [
                Parameter("Level"),
                Parameter("name", precedence: PrecedenceDefinition.Required),
                Parameter("language", dataType: "choice", precedence: PrecedenceDefinition.Implicit),
                Parameter("FuncStack", type: "bind"),
            ],
            metadata);

        act.Should().Throw<InvalidTemplateMetadataException>().Which.Message.Should().Contain($"'{symbol}'");
    }

    [Fact]
    public void Project_SeveralUnmatchedSymbols_ReportsAllInOrder()
    {
        FuncHostTemplateMetadata metadata = Metadata(("zeta", Symbol()), ("alpha", Symbol()));

        Action act = () => TemplateParameterProjector.Project([Parameter("Level")], metadata);

        act.Should().Throw<InvalidTemplateMetadataException>().Which.Message.Should().Contain("'alpha', 'zeta'");
    }

    [Fact]
    public void Project_UnmatchedSymbolAndInvalidCanonicalName_ReportsUnmatchedSymbol()
    {
        FuncHostTemplateMetadata metadata = Metadata(("log_level", Symbol(longName: "log-level")));

        Action act = () => TemplateParameterProjector.Project([Parameter("log level")], metadata);

        act.Should().Throw<InvalidTemplateMetadataException>().Which.Message.Should().Contain("'log_level'");
    }

    [Theory]
    [InlineData("--log-level", null)]
    [InlineData("log level", null)]
    [InlineData("log:level", null)]
    [InlineData("log=level", null)]
    [InlineData(null, "-l")]
    [InlineData(null, "l l")]
    public void Project_InvalidAlias_Throws(string? longName, string? shortName)
    {
        FuncHostTemplateMetadata metadata = Metadata(("Level", Symbol(longName, shortName)));

        Action act = () => TemplateParameterProjector.Project([Parameter("Level")], metadata);

        act.Should().Throw<InvalidTemplateMetadataException>().Which.Message.Should().Contain($"'{longName ?? shortName}'");
    }

    [Fact]
    public void Project_InvalidCanonicalNameWithoutLongName_Throws()
    {
        Action act = () => TemplateParameterProjector.Project([Parameter("log:level")], FuncHostTemplateMetadata.Empty);

        act.Should().Throw<InvalidTemplateMetadataException>().Which.Message.Should().Contain("'log:level'");
    }

    [Fact]
    public void Project_InvalidCanonicalNameWithLongName_UsesLongName()
    {
        FuncHostTemplateMetadata metadata = Metadata(("log:level", Symbol(longName: "log-level")));

        IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project([Parameter("log:level")], metadata);

        TemplateParameterDefinition definition = definitions.Should().ContainSingle().Subject;
        definition.CanonicalName.Should().Be("log:level");
        definition.LongName.Should().Be("log-level");
    }

    [Fact]
    public void Project_SharedLongName_Throws()
    {
        FuncHostTemplateMetadata metadata = Metadata(("Level", Symbol(longName: "Verbosity")));

        Action act = () => TemplateParameterProjector.Project([Parameter("Level"), Parameter("Verbosity")], metadata);

        act.Should().Throw<InvalidTemplateMetadataException>().Which.Message.Should().Contain("'Level' and 'Verbosity'");
    }

    [Fact]
    public void Project_SharedShortName_Throws()
    {
        FuncHostTemplateMetadata metadata = Metadata(("Level", Symbol(shortName: "l")), ("Language", Symbol(shortName: "l")));

        Action act = () => TemplateParameterProjector.Project([Parameter("Level"), Parameter("Language")], metadata);

        act.Should().Throw<InvalidTemplateMetadataException>().Which.Message.Should().Contain("'Level' and 'Language'");
    }

    [Fact]
    public async Task Project_InstalledTemplate_ReturnsSameUserParametersBeforeAndAfterCacheReload()
    {
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), hive);
        TemplateParameterDefinition[] expected =
        [
            new(
                CanonicalName: "Level",
                LongName: "log-level",
                ShortName: "l",
                Description: null,
                DataType: "choice",
                Choices: [new("Low", DisplayName: null, "Low level"), new("High", DisplayName: null, Description: null)],
                DefaultValue: "Low",
                IsRequired: false,
                IsHidden: false,
                AlwaysShow: false),
            new(
                CanonicalName: "Namespace",
                LongName: "Namespace",
                ShortName: null,
                Description: null,
                DataType: "string",
                Choices: [],
                DefaultValue: "Company.Function",
                IsRequired: false,
                IsHidden: false,
                AlwaysShow: false),
        ];

        string[] loads = ["scan", "cache"];
        foreach (string load in loads)
        {
            ITemplateInfo template = await TemplateFixtures.GetInstalledTemplateAsync(hive, TemplateFixtures.MetadataTemplateIdentity);

            IReadOnlyList<TemplateParameterDefinition> definitions = TemplateParameterProjector.Project(
                template.ParameterDefinitions,
                FuncHostTemplateMetadataReader.Read(template));

            definitions.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering(), "the {0} load", load);
        }
    }

    [Fact]
    public void Project_NullParameters_Throws()
    {
        Action act = () => TemplateParameterProjector.Project(null!, FuncHostTemplateMetadata.Empty);

        act.Should().Throw<ArgumentNullException>().WithParameterName("parameters");
    }

    [Fact]
    public void Project_NullMetadata_Throws()
    {
        Action act = () => TemplateParameterProjector.Project([], null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("metadata");
    }

    private static ITemplateParameter Parameter(
        string name,
        string type = "parameter",
        string? dataType = "string",
        PrecedenceDefinition precedence = PrecedenceDefinition.Optional,
        string? description = null,
        string? defaultValue = null)
    {
        ITemplateParameter parameter = Substitute.For<ITemplateParameter>();
        parameter.Name.Returns(name);
        parameter.Type.Returns(type);
        parameter.DataType.Returns(dataType);
        parameter.Precedence.Returns(new TemplateParameterPrecedence(precedence, isRequired: precedence == PrecedenceDefinition.Required));
        parameter.Description.Returns(description);
        parameter.DefaultValue.Returns(defaultValue);
        parameter.Choices.Returns((IReadOnlyDictionary<string, ParameterChoice>?)null);
        return parameter;
    }

    private static FuncHostSymbolMetadata Symbol(string? longName = null, string? shortName = null, bool isHidden = false, bool alwaysShow = false)
        => new(longName, shortName, isHidden, alwaysShow);

    private static FuncHostTemplateMetadata Metadata(params (string Name, FuncHostSymbolMetadata Symbol)[] symbols)
        => new(IsHidden: false, symbols.ToDictionary(symbol => symbol.Name, symbol => symbol.Symbol, StringComparer.Ordinal));
}
