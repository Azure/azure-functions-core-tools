// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateEngineExtensionsTests
{
    [Fact]
    public void ReadHostData_CachedHostData_ParsesMetadataWithoutMounting()
    {
        var template = CachedTemplate(
            """
            {
              // Dotnet-shaped host metadata.
              "SymbolInfo": {
                "AuthorizationLevel": { "longName": "auth-level", "shortName": "a", "alwaysShow": "True" },
                "Internal": { "longName": "", "shortName": null, "isHidden": true },
                "Other": { "isHidden": false, "alwaysShow": "false", "futureProperty": 42 },
              },
              "UsageExamples": [ "func new --auth-level anonymous" ],
              "IsHidden": "true",
              "futureProperty": {},
            }
            """);
        template.HostConfigPlace.Returns(".template.config/func.host.json");
        var settings = Substitute.For<IEngineEnvironmentSettings>();

        FuncTemplateHostData hostData = settings.ReadHostData(template);

        hostData.IsHidden.Should().BeTrue();
        hostData.UsageExamples.Should().Equal("func new --auth-level anonymous");
        hostData.SymbolInfo.Should().BeEquivalentTo(new Dictionary<string, FuncTemplateSymbol>
        {
            ["AuthorizationLevel"] = new("AuthorizationLevel")
            {
                LongName = "auth-level",
                ShortName = "a",
                AlwaysShow = true,
            },
            ["Internal"] = new("Internal")
            {
                LongName = "",
                ShortName = null,
                IsHidden = true,
            },
            ["Other"] = new("Other"),
        });
        settings.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void ReadHostData_NoHostConfiguration_UsesEmptyMetadata()
    {
        var settings = Substitute.For<IEngineEnvironmentSettings>();
        FuncTemplateHostData hostData = settings.ReadHostData(Substitute.For<ITemplateInfo>());

        hostData.SymbolInfo.Should().BeEmpty();
        hostData.UsageExamples.Should().BeEmpty();
        hostData.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void ReadHostData_EmptyHostObject_UsesEmptyMetadata()
    {
        var settings = Substitute.For<IEngineEnvironmentSettings>();
        FuncTemplateHostData hostData = settings.ReadHostData(CachedTemplate("{}"));

        hostData.SymbolInfo.Should().BeEmpty();
        hostData.IsHidden.Should().BeFalse();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"symbolInfo":[]}""")]
    [InlineData("""{"symbolInfo":null}""")]
    [InlineData("""{"symbolInfo":{"param":null}}""")]
    [InlineData("""{"symbolInfo":{"param":"invalid"}}""")]
    [InlineData("""{"symbolInfo":{"param":{"isHidden":null}}}""")]
    [InlineData("""{"symbolInfo":{"param":{"longName":42}}}""")]
    [InlineData("""{"symbolInfo":{"param":{"shortName":false}}}""")]
    [InlineData("""{"symbolInfo":{"param":{"isHidden":"invalid"}}}""")]
    [InlineData("""{"symbolInfo":{"param":{"alwaysShow":[]}}}""")]
    [InlineData("""{"isHidden":42}""")]
    [InlineData("""{"usageExamples":null}""")]
    [InlineData("""{"usageExamples":{}}""")]
    [InlineData("""{"usageExamples":[false]}""")]
    public void ReadHostData_InvalidCachedData_ThrowsWithContextWithoutFallback(string json)
    {
        var template = CachedTemplate(json);
        template.Identity.Returns("Func.Tests.Invalid");
        template.HostConfigPlace.Returns(".template.config/func.host.json");
        var settings = Substitute.For<IEngineEnvironmentSettings>();

        Action act = () => settings.ReadHostData(template);

        act.Should().Throw<JsonException>()
            .WithMessage("*Func.Tests.Invalid*.template.config/func.host.json*")
            .WithInnerException<JsonException>();
        settings.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ReadHostData_UnavailableCache_ReadsMountedFileAndDisposesResources(string? cachedData)
    {
        var template = CachedTemplate(cachedData);
        var (settings, mount, file) = MountedHost(template);
        MemoryStream stream = new(Encoding.UTF8.GetBytes("""{"symbolInfo":{"param":{"shortName":"p"}}}"""));
        file.OpenRead().Returns(stream);

        FuncTemplateHostData hostData = settings.ReadHostData(template);

        hostData.SymbolInfo["param"].ShortName.Should().Be("p");
        mount.Received(1).Dispose();
        stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public void ReadHostData_TemplateWithoutCacheInterface_ReadsMountedFile()
    {
        var template = Substitute.For<ITemplateInfo>();
        var (settings, mount, file) = MountedHost(template);
        file.OpenRead().Returns(new MemoryStream(Encoding.UTF8.GetBytes("""{"isHidden":true}""")));

        FuncTemplateHostData hostData = settings.ReadHostData(template);

        hostData.IsHidden.Should().BeTrue();
        mount.Received(1).Dispose();
    }

    [Fact]
    public void ReadHostData_InvalidMountedData_ThrowsAndDisposesResources()
    {
        var template = CachedTemplate(null);
        var (settings, mount, file) = MountedHost(template);
        MemoryStream stream = new(Encoding.UTF8.GetBytes("{"));
        file.OpenRead().Returns(stream);

        Action act = () => settings.ReadHostData(template);

        act.Should().Throw<JsonException>().WithMessage("*Func.Tests.Mounted*.template.config/func.host.json*");
        mount.Received(1).Dispose();
        stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public void ReadHostData_MissingDeclaredFile_ThrowsAndDisposesMount()
    {
        var template = CachedTemplate(null);
        var (settings, mount, file) = MountedHost(template);
        file.Exists.Returns(false);

        Action act = () => settings.ReadHostData(template);

        act.Should().Throw<FileNotFoundException>().WithMessage("*Func.Tests.Mounted*.template.config/func.host.json*");
        mount.Received(1).Dispose();
    }

    [Fact]
    public void ReadHostData_UnreadableDeclaredFile_ThrowsAndDisposesMount()
    {
        var template = CachedTemplate(null);
        var (settings, mount, file) = MountedHost(template);
        file.OpenRead().Returns(_ => throw new UnauthorizedAccessException("Denied"));

        Action act = () => settings.ReadHostData(template);

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*Func.Tests.Mounted*.template.config/func.host.json*")
            .WithInnerException<UnauthorizedAccessException>();
        mount.Received(1).Dispose();
    }

    [Fact]
    public void ReadHostData_UnavailableMount_ThrowsWithContext()
    {
        var template = CachedTemplate(null);
        var (settings, _, _) = MountedHost(template);
        settings.Components.OfType<IMountPointFactory>().Returns([]);

        Action act = () => settings.ReadHostData(template);

        act.Should().Throw<IOException>().WithMessage("*Func.Tests.Mounted*.template.config/func.host.json*");
    }

    private static ITemplateInfo CachedTemplate(string? json)
    {
        var template = Substitute.For<ITemplateInfo, ITemplateInfoHostJsonCache>();
        ((ITemplateInfoHostJsonCache)template).HostData.Returns(json);
        return template;
    }

    private static (IEngineEnvironmentSettings Settings, IMountPoint Mount, IFile File) MountedHost(ITemplateInfo template)
    {
        template.Identity.Returns("Func.Tests.Mounted");
        template.MountPointUri.Returns("test-mount");
        template.HostConfigPlace.Returns(".template.config/func.host.json");
        var settings = Substitute.For<IEngineEnvironmentSettings>();
        var factory = Substitute.For<IMountPointFactory>();
        var mount = Substitute.For<IMountPoint>();
        var file = Substitute.For<IFile>();
        file.Exists.Returns(true);
        mount.FileInfo(".template.config/func.host.json").Returns(file);
        settings.Components.OfType<IMountPointFactory>().Returns([factory]);
        factory.TryMount(settings, null, template.MountPointUri, out Arg.Any<IMountPoint?>()).Returns(call =>
        {
            call[3] = mount;
            return true;
        });
        return (settings, mount, file);
    }
}
