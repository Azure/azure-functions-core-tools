// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Search;

namespace Azure.Functions.Cli.Tests.Templates.Search;

public class FuncSearchIndexReaderTests
{
    // A trimmed copy of the real NuGetTemplateSearchInfoVer2.json schema the discovery tool emits.
    private const string ValidVer2Json = """
    {
      "Version": "2.0",
      "TemplatePackages": [
        {
          "Name": "Microsoft.Azure.Functions.Templates.Node",
          "Version": "1.0.0",
          "Owners": "Microsoft",
          "Description": "Node.js function and project templates.",
          "Templates": [
            {
              "Identity": "Azure.Functions.Node.HttpTrigger.JavaScript.1.0",
              "GroupIdentity": "Azure.Functions.Node.HttpTrigger",
              "Name": "HTTP trigger",
              "ShortNameList": [ "http", "HttpTrigger" ],
              "Author": "Microsoft",
              "Description": "A function that is triggered by HTTP requests.",
              "Classifications": [ "Azure Function", "Trigger", "Http" ],
              "TagsCollection": {
                "language": "javascript",
                "type": "item",
                "azfunc-stack": "node",
                "azfunc-trigger": "http"
              }
            }
          ]
        },
        {
          "Name": "Contoso.Templates",
          "Version": "2.1.0",
          "Owners": [ "Contoso", "Fabrikam" ],
          "Templates": []
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ValidVer2_ReadsPackagesTemplatesAndTags()
    {
        FuncSearchIndex index = FuncSearchIndexReader.Parse(ValidVer2Json);

        index.Version.Should().Be("2.0");
        index.Packages.Should().HaveCount(2);

        FuncSearchPackage node = index.Packages[0];
        node.Name.Should().Be("Microsoft.Azure.Functions.Templates.Node");
        node.Version.Should().Be("1.0.0");
        node.Owners.Should().ContainSingle().Which.Should().Be("Microsoft");
        node.Templates.Should().ContainSingle();

        FuncSearchTemplate template = node.Templates[0];
        template.Identity.Should().Be("Azure.Functions.Node.HttpTrigger.JavaScript.1.0");
        template.Name.Should().Be("HTTP trigger");
        template.ShortNameList.Should().Equal("http", "HttpTrigger");
        template.Classifications.Should().Contain("Http");
        template.Tags.Should().Contain(new KeyValuePair<string, string>("azfunc-stack", "node"));
        template.Tags.Should().Contain(new KeyValuePair<string, string>("language", "javascript"));
    }

    [Fact]
    public void Parse_OwnersArray_ReadsAllOwners()
    {
        FuncSearchIndex index = FuncSearchIndexReader.Parse(ValidVer2Json);

        FuncSearchPackage contoso = index.Packages[1];
        contoso.Owners.Should().Equal("Contoso", "Fabrikam");
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsFormatException()
    {
        Action act = () => FuncSearchIndexReader.Parse("{ not json");

        act.Should().Throw<FuncSearchIndexFormatException>();
    }

    [Fact]
    public void Parse_MissingVersion_ThrowsFormatException()
    {
        Action act = () => FuncSearchIndexReader.Parse("""{ "TemplatePackages": [] }""");

        act.Should().Throw<FuncSearchIndexFormatException>().WithMessage("*Version*");
    }

    [Fact]
    public void Parse_UnsupportedVersion_ThrowsFormatException()
    {
        Action act = () => FuncSearchIndexReader.Parse("""{ "Version": "1.0.0.0", "TemplatePackages": [] }""");

        act.Should().Throw<FuncSearchIndexFormatException>().WithMessage("*1.0.0.0*");
    }

    [Fact]
    public void Parse_MissingTemplatePackages_ThrowsFormatException()
    {
        Action act = () => FuncSearchIndexReader.Parse("""{ "Version": "2.0" }""");

        act.Should().Throw<FuncSearchIndexFormatException>().WithMessage("*TemplatePackages*");
    }
}
