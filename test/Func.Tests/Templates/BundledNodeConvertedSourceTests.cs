// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Templates.Node;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("TimerTrigger-JavaScript")]
    [InlineData("HttpTrigger-JavaScript")]
    [InlineData("HttpTrigger-TypeScript")]
    [InlineData("DaprPublishOutputBinding-JavaScript")]
    [InlineData("DaprServiceInvocationTrigger-JavaScript")]
    [InlineData("EventHubTrigger-JavaScript")]
    [InlineData("ServiceBusQueueTrigger-JavaScript")]
    [InlineData("ServiceBusTopicTrigger-JavaScript")]
    [InlineData("CosmosDBTrigger-TypeScript")]
    public async Task ConvertedSource_ActualArchive_UsesHostProofWithoutReconstructingTheConversion(string identity)
    {
        NodeConvertedSourceAuthority authority = new();

        NodeItemSource source = await ReadQualifiedSourceAsync(identity, authority);

        source.HasHostSourceQualification.Should().BeTrue();
        authority.Matches(source).Should().BeTrue();
        NodeItemSourceQualifier.ValidateDaprInvocation(source, "ApprovedName").Should().BeNull();
        Directory.Exists(Path.Combine(_root, "src")).Should().BeFalse();
    }

    [Theory]
    [InlineData("nativeBody")]
    [InlineData("configuration")]
    [InlineData("host")]
    [InlineData("sourcePath")]
    [InlineData("configurationPath")]
    [InlineData("rawBody")]
    [InlineData("rawAction")]
    [InlineData("originalPin")]
    [InlineData("missingPin")]
    [InlineData("foreignIdentity")]
    [InlineData("rewrittenBody")]
    [InlineData("rewrittenConfiguration")]
    [InlineData("rewrittenHost")]
    public async Task ConvertedSource_ChangedAssociation_CannotBorrowHostProof(string corruption)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync();
        NodeConvertedSourceAuthority authority = new();
        if (corruption is "nativeBody" or "rewrittenBody") source = source with { Content = Encoding.UTF8.GetBytes("unapproved native body") };
        else if (corruption is "configuration" or "rewrittenConfiguration") source = source with { Configuration = Encoding.UTF8.GetBytes("{}") };
        else if (corruption is "host" or "rewrittenHost") source = source with { HostConfiguration = Encoding.UTF8.GetBytes("{}") };
        else if (corruption == "sourcePath") source = source with { ContentPath = "content/foreign.js" };
        else if (corruption == "configurationPath") source = source with { ConfigurationPath = "content/foreign/template.json" };
        else if (corruption == "rawBody") source.Template.Files!["function.js"] += "\n// unreviewed";
        else if (corruption == "rawAction") source.Template.Actions![1].FilePath = "foreign.js";
        else if (corruption == "missingPin") source = source with { Sources = source.Sources.Skip(1).ToArray() };
        else
        {
            source = source with
            {
                Sources = source.Sources.Select(pin => pin.Identity.EndsWith("/templates/templates.json", StringComparison.Ordinal)
                    ? pin with { Sha256 = corruption == "originalPin" ? new string('0', 64) : pin.Sha256,
                        TemplateIdentity = corruption == "foreignIdentity" ? "Foreign" : pin.TemplateIdentity } : pin).ToArray(),
            };
        }
        if (corruption.StartsWith("rewritten", StringComparison.Ordinal))
        {
            string path = corruption switch
            {
                "rewrittenBody" => source.ContentPath,
                "rewrittenConfiguration" => source.ConfigurationPath,
                _ => source.ConfigurationPath.Replace("template.json", "func.host.json", StringComparison.Ordinal),
            };
            ReadOnlyMemory<byte> bytes = corruption switch
            {
                "rewrittenBody" => source.Content,
                "rewrittenConfiguration" => source.Configuration,
                _ => source.HostConfiguration,
            };
            string hash = Convert.ToHexString(SHA256.HashData(bytes.Span));
            source = source with
            {
                Sources = source.Sources.Select(pin => pin.Identity == path ? pin with { Sha256 = hash } : pin).ToArray(),
                ContentSource = source.ContentSource.Identity == path ? source.ContentSource with { Sha256 = hash } : source.ContentSource,
            };
        }

        authority.Matches(source).Should().BeFalse();
    }

    [Fact]
    public async Task ConvertedSource_HistoricalEncoding_RetainsTheExistingQualifier()
    {
        NodeItemSource source = HistoricalSource(await ReadQualifiedSourceAsync());
        NodeConvertedSourceAuthority authority = new();

        authority.Matches(source).Should().BeFalse();
        Action legacy = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        legacy.Should().NotThrow();
        source.HasHostSourceQualification.Should().BeFalse();
    }

    [Fact]
    public async Task ConvertedSource_OldUnsafeDaprBody_CannotClaimCurrentHostQualification()
    {
        NodeItemSource source = await ReadQualifiedSourceAsync("DaprPublishOutputBinding-JavaScript");
        string body = source.Template.Files!.Single().Value.Replace("$(FUNCTION_NAME_INPUT)", "funcNodeItem", StringComparison.Ordinal);
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        source = source with
        {
            Content = bytes,
            ContentSource = source.ContentSource with { Sha256 = hash },
            Sources = source.Sources.Select(pin => pin.Identity == source.ContentPath ? pin with { Sha256 = hash } : pin).ToArray(),
        };

        new NodeConvertedSourceAuthority().Matches(source).Should().BeFalse();
        Action sourceAssociation = () => NodeItemSourceQualifier.Validate(source, "JavaScript");
        Action invocation = () => NodeItemSourceQualifier.ValidateDaprInvocation(source, "ApprovedName");

        sourceAssociation.Should().NotThrow("the historical source cohort remains identifiable");
        invocation.Should().Throw<NodeItemHelperCompatibilityException>("its fixed helper name is still unsafe");
    }

    [Fact]
    public async Task ConvertedSource_NewNativeSymbolName_NeedsDataQualificationNotAnotherRuntimeTransformation()
    {
        NodeItemSource original = await ReadQualifiedSourceAsync();
        JsonNode config = JsonNode.Parse(original.Configuration.Span)!;
        JsonObject symbols = config["symbols"]!.AsObject();
        JsonNode derived = symbols["timerTrigger-schedule-NodeString"]!.DeepClone();
        symbols.Remove("timerTrigger-schedule-NodeString");
        symbols["NativeSchedule"] = derived;
        byte[] bytes = Encoding.UTF8.GetBytes(config.ToJsonString());
        NodeItemSource changed = original with
        {
            Configuration = bytes,
            Sources = original.Sources.Select(pin => pin.Identity == original.ConfigurationPath
                ? pin with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) } : pin).ToArray(),
        };
        var approved = new
        {
            schemaVersion = 1,
            cohorts = new[]
            {
                new
                {
                    packageId = changed.Contract.Package!.Identifier,
                    sources = changed.Sources.Where(pin => pin.Identity.StartsWith("provenance/node/v2/", StringComparison.Ordinal))
                        .Select(pin => new { path = pin.Identity, sha256 = pin.Sha256 }),
                    templates = new[]
                    {
                        new
                        {
                            identity = changed.Contract.Source.TemplateIdentity,
                            legacyId = changed.Template.Id,
                            configuration = new { path = changed.ConfigurationPath, definition = config },
                            host = new { path = changed.ConfigurationPath.Replace("template.json", "func.host.json", StringComparison.Ordinal),
                                definition = JsonNode.Parse(changed.HostConfiguration.Span) },
                            content = new { path = changed.ContentPath, sha256 = Convert.ToHexString(SHA256.HashData(
                                Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(changed.Content.Span).ReplaceLineEndings("\n")))) },
                        },
                    },
                },
            },
        };
        NodeConvertedSourceAuthority qualified = new(JsonSerializer.SerializeToUtf8Bytes(approved));
        Action legacy = () => NodeItemSourceQualifier.Validate(changed, "JavaScript");

        legacy.Should().Throw<InvalidDataException>().WithMessage("*property symbols*");
        new NodeConvertedSourceAuthority().Matches(changed).Should().BeFalse("package metadata cannot replace the host's approved definition");
        qualified.Matches(changed).Should().BeTrue("the approved data pins native semantics without knowing the derived symbol's name");
    }

    [Theory]
    [InlineData("DaprPublishOutputBinding-JavaScript", "host")]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", "HOST")]
    [InlineData("DaprPublishOutputBinding-JavaScript", null)]
    public async Task ConvertedSource_ApprovedDaprContent_RetainsInvocationNamingGuards(string identity, string? name)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync(identity, new());

        string? diagnostic = NodeItemSourceQualifier.ValidateDaprInvocation(source, name);

        diagnostic.Should().NotBeNull();
    }

    [Fact]
    public void ConvertedSource_InvalidContracts_AreExplicitErrors()
    {
        NodeConvertedSourceAuthority authority = new();
        Action missing = () => authority.Matches(null!);
        Action wrongVersion = () => new NodeConvertedSourceAuthority(Encoding.UTF8.GetBytes("""{"schemaVersion":2,"cohorts":[]}"""));
        Action empty = () => new NodeConvertedSourceAuthority(Encoding.UTF8.GetBytes("""{"schemaVersion":1,"cohorts":[]}"""));

        missing.Should().Throw<ArgumentNullException>();
        wrongVersion.Should().Throw<InvalidDataException>();
        empty.Should().Throw<InvalidDataException>();
    }
}
