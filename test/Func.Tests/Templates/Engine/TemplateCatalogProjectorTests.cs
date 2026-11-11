// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateCatalogProjectorTests
{
    private const string HostConfigPlace = "/template/.template.config/func.host.json";

    [Theory]
    [InlineData("item")]
    [InlineData("Item")]
    public void GetTemplateType_ItemType_ReturnsItem(string type)
    {
        ITemplateMetadata template = Template(type: type);

        TemplateType? templateType = TemplateCatalogProjector.GetTemplateType(template);

        templateType.Should().Be(TemplateType.Item);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("PROJECT")]
    public void GetTemplateType_ProjectType_ReturnsProject(string type)
    {
        ITemplateMetadata template = Template(type: type);

        TemplateType? templateType = TemplateCatalogProjector.GetTemplateType(template);

        templateType.Should().Be(TemplateType.Project);
    }

    [Theory]
    [InlineData("solution")]
    [InlineData("")]
    [InlineData(null)]
    public void GetTemplateType_OtherOrMissingType_ReturnsNull(string? type)
    {
        ITemplateMetadata template = Template(type: type);

        TemplateType? templateType = TemplateCatalogProjector.GetTemplateType(template);

        templateType.Should().BeNull();
    }

    [Fact]
    public void Project_Template_CopiesTemplateDetails()
    {
        ITemplateInfo template = Template(
            type: "project",
            language: "Python",
            description: "Creates a project",
            groupIdentity: "Func.Tests.Group");

        TemplateCatalogEntry entry = TemplateCatalogProjector.Project(template, Package("Contoso.Functions.Templates", "1.2.3"));

        entry.Should().BeEquivalentTo(new TemplateCatalogEntry(
            identity: "Func.Tests.Template",
            name: "Func Tests Template",
            description: "Creates a project",
            type: TemplateType.Project,
            shortNames: ["func-tests-template", "ftt"],
            groupIdentity: "Func.Tests.Group",
            language: "Python",
            precedence: 100,
            package: new TemplatePackageInfo("Contoso.Functions.Templates", "1.2.3"),
            isHidden: false,
            parameters: [],
            metadataDiagnostic: null)
        {
            EngineTemplate = template,
        });
    }

    [Fact]
    public void TemplateCatalogEntry_SnapshotsCallerOwnedCollections()
    {
        List<string> shortNames = ["template"];
        List<TemplateParameterChoice> choices = [new("one", "One", null)];
        List<TemplateParameterDefinition> parameters =
        [
            new("choice", "choice", null, null, "choice", choices, null, false, false, false),
        ];

        TemplateCatalogEntry entry = new(
            identity: "Func.Tests.Template",
            name: "Template",
            description: null,
            type: TemplateType.Item,
            shortNames: shortNames,
            groupIdentity: null,
            language: null,
            precedence: 0,
            package: null,
            isHidden: false,
            parameters: parameters,
            metadataDiagnostic: null);
        shortNames.Add("later");
        choices.Add(new("two", "Two", null));
        parameters.Clear();

        entry.ShortNames.Should().Equal("template");
        entry.Parameters.Should().ContainSingle();
        entry.Parameters[0].Choices.Should().ContainSingle().Which.Value.Should().Be("one");
    }

    [Fact]
    public void Project_EmptyOptionalDetails_AreNull()
    {
        ITemplateInfo template = Template(language: string.Empty, description: string.Empty, groupIdentity: string.Empty);

        TemplateCatalogEntry entry = TemplateCatalogProjector.Project(template, package: null);

        entry.Description.Should().BeNull();
        entry.GroupIdentity.Should().BeNull();
        entry.Language.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Project_PackageWithoutVersion_ReportsNoVersion(string? version)
    {
        TemplateCatalogEntry entry = TemplateCatalogProjector.Project(Template(), Package("/templates/contoso", version));

        entry.Package.Should().Be(new TemplatePackageInfo("/templates/contoso", Version: null));
    }

    [Fact]
    public void Project_HiddenTemplate_IsHidden()
    {
        TemplateCatalogEntry entry = TemplateCatalogProjector.Project(Template(hostData: """{ "isHidden": true }"""), package: null);

        entry.IsHidden.Should().BeTrue();
        entry.MetadataDiagnostic.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    public void Project_MalformedFuncHostMetadata_ReportsDiagnostic(string? hostData)
    {
        ITemplateInfo template = Template(hostData: hostData);
        template.HostConfigPlace.Returns(HostConfigPlace);

        TemplateCatalogEntry entry = TemplateCatalogProjector.Project(template, package: null);

        entry.MetadataDiagnostic.Should().StartWith("func.host.json");
        entry.MetadataDiagnosticCause.Should().BeOfType<InvalidTemplateMetadataException>();
        entry.Parameters.Should().BeEmpty();
        entry.IsHidden.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{ "isHidden": true, "symbolInfo": { "Level": { "longName": "" } } }""")]
    [InlineData("""{ "isHidden": true, "symbolInfo": { "Level": { "longName": "log-level" } } }""")]
    public void Project_UnusableFuncHostMetadata_AppliesNoneOfIt(string hostData)
    {
        TemplateCatalogEntry entry = TemplateCatalogProjector.Project(Template(hostData: hostData), package: null);

        entry.MetadataDiagnostic.Should().Contain("'Level'");
        entry.Parameters.Should().BeEmpty();
        entry.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Project_TemplateWithoutItemOrProjectType_Throws()
    {
        Action act = () => TemplateCatalogProjector.Project(Template(type: "solution"), package: null);

        act.Should().Throw<ArgumentException>().WithParameterName("template");
    }

    [Fact]
    public void GetTemplateType_NullTemplate_Throws()
    {
        Action act = () => TemplateCatalogProjector.GetTemplateType(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("template");
    }

    [Fact]
    public void Project_NullTemplate_Throws()
    {
        Action act = () => TemplateCatalogProjector.Project(null!, package: null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("template");
    }

    private static ITemplateInfo Template(
        string identity = "Func.Tests.Template",
        string? type = "item",
        string? language = null,
        string? description = null,
        string? groupIdentity = null,
        string? hostData = null,
        string mountPoint = "/packages/templates")
    {
        Dictionary<string, string> tags = [];
        if (type is not null)
        {
            tags["type"] = type;
        }

        if (language is not null)
        {
            tags["language"] = language;
        }

        ITemplateInfo template = Substitute.For<ITemplateInfo, ITemplateInfoHostJsonCache>();
        template.Identity.Returns(identity);
        template.Name.Returns("Func Tests Template");
        template.Description.Returns(description);
        template.ShortNameList.Returns(["func-tests-template", "ftt"]);
        template.GroupIdentity.Returns(groupIdentity);
        template.Precedence.Returns(100);
        template.TagsCollection.Returns(tags);
        template.ParameterDefinitions.Returns(ParameterDefinitionSet.Empty);
        template.MountPointUri.Returns(mountPoint);
        template.HostConfigPlace.Returns(hostData is null ? null : HostConfigPlace);
        ((ITemplateInfoHostJsonCache)template).HostData.Returns(hostData);
        return template;
    }

    private static IManagedTemplatePackage Package(string identifier, string? version, string mountPoint = "/packages/templates")
    {
        IManagedTemplatePackage package = Substitute.For<IManagedTemplatePackage>();
        package.Identifier.Returns(identifier);
        package.Version.Returns(version);
        package.MountPointUri.Returns(mountPoint);
        return package;
    }
}
