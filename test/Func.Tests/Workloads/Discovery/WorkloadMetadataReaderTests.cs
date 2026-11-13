// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Workloads.Discovery;

public class WorkloadMetadataReaderTests : IDisposable
{
    private const string SchemaUrl = WorkloadManifestSchema.PackageManifestV1Schema;

    private readonly string _tempDir;
    private readonly WorkloadMetadataReader _reader = new();

    public WorkloadMetadataReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsMetadata_WhenManifestIsValid()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.Schema.Should().Be(SchemaUrl);
        metadata.Kind.Should().Be(WorkloadKind.Workload);
        metadata.EntryPoint!.AssemblyPath.Should().Be("Foo.dll");
        metadata.EntryPoint.Type.Should().Be("Foo.MyWorkload");
    }

    [Theory]
    [InlineData("templates", "templates")]
    [InlineData("content/templates/", "content/templates")]
    [InlineData("content\\templates\\", "content/templates")]
    [InlineData("tools/any/templates///", "tools/any/templates")]
    [InlineData("templates/../other", "other")]
    [InlineData("templates/./other", "templates/other")]
    [InlineData("templates\\..\\other", "other")]
    [InlineData("./templates", "templates")]
    [InlineData("templates//other", "templates/other")]
    [InlineData("templates\\\\other", "templates/other")]
    public void Read_NormalizesTemplateRoot(string path, string expected)
    {
        WriteTemplateMetadata("workload", JsonSerializer.Serialize(path));

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.TemplateRoot.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/")]
    [InlineData("/templates")]
    [InlineData("\\templates")]
    [InlineData("\\\\server\\templates")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../other")]
    [InlineData("..\\other")]
    [InlineData("templates/../../other")]
    [InlineData("templates/..")]
    [InlineData("templates/a\u0000b")]
    public void Read_RejectsInvalidTemplateRoot(string path)
    {
        WriteTemplateMetadata("workload", JsonSerializer.Serialize(path));

        var exception = FluentActions.Invoking(() => _reader.Read(_tempDir))
            .Should().ThrowExactly<InvalidWorkloadException>().Which;

        exception.Message.Should().Contain(Path.Combine(_tempDir, "workload.json")).And.Contain("templateRoot");
        exception.InnerException.Should().BeAssignableTo<ArgumentException>();
    }

    [Theory]
    [InlineData("C:/templates")]
    [InlineData("C:templates")]
    public void Read_UsesPlatformRootedPathRules(string path)
    {
        WriteTemplateMetadata("workload", JsonSerializer.Serialize(path));

        if (OperatingSystem.IsWindows())
        {
            FluentActions.Invoking(() => _reader.Read(_tempDir))
                .Should().ThrowExactly<InvalidWorkloadException>()
                .WithMessage("*templateRoot*relative to the package root*");
        }
        else
        {
            _reader.Read(_tempDir).TemplateRoot.Should().Be(path);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"templates\"]")]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("true")]
    public void Read_RejectsNonStringTemplateRoot(string json)
    {
        WriteTemplateMetadata("workload", json);

        var exception = FluentActions.Invoking(() => _reader.Read(_tempDir))
            .Should().ThrowExactly<InvalidWorkloadException>().Which;

        exception.Message.Should().Contain("workload.json").And.Contain("templateRoot");
        exception.InnerException.Should().BeOfType<JsonException>();
    }

    [Theory]
    [InlineData("content")]
    [InlineData("meta")]
    [InlineData("rid-pointer")]
    public void Read_RejectsTemplateRootOnNonRuntimeKinds(string kind)
    {
        WriteTemplateMetadata(kind, "\"templates\"");

        FluentActions.Invoking(() => _reader.Read(_tempDir))
            .Should().ThrowExactly<InvalidWorkloadException>()
            .WithMessage("*workload.json*templateRoot*only supported for kind 'workload'*");
    }

    [Theory]
    [InlineData("workload", null)]
    [InlineData("workload", "null")]
    [InlineData("content", null)]
    [InlineData("content", "null")]
    [InlineData("meta", null)]
    [InlineData("meta", "null")]
    [InlineData("rid-pointer", null)]
    [InlineData("rid-pointer", "null")]
    public void Read_AcceptsAbsentOrNullTemplateRoot(string kind, string? json)
    {
        WriteTemplateMetadata(kind, json);

        _reader.Read(_tempDir).TemplateRoot.Should().BeNull();
    }

    [Fact]
    public void Read_RejectsTemplateRootResolvingToSiblingWithSharedPrefix()
    {
        string path = $"../{Path.GetFileName(_tempDir)}-other/templates";
        WriteTemplateMetadata("workload", JsonSerializer.Serialize(path));

        FluentActions.Invoking(() => _reader.Read(_tempDir))
            .Should().ThrowExactly<InvalidWorkloadException>()
            .WithMessage("*templateRoot*inside the package root*");
    }

    [Fact]
    public void NormalizePackagePath_RejectsPackageRootEvenWhenItIsAVolumeRoot()
    {
        string packageDirectory = Path.GetPathRoot(_tempDir)!;

        FluentActions.Invoking(() => WorkloadMetadata.NormalizePackagePath(".", packageDirectory))
            .Should().ThrowExactly<ArgumentException>()
            .WithMessage("*inside the package root*");
    }

    [Fact]
    public void NormalizePackagePath_UsesPlatformCaseComparison()
    {
        string packageDirectory = Path.Combine(_tempDir, "package");
        const string path = "../PACKAGE/templates";

        if (OperatingSystem.IsWindows())
        {
            WorkloadMetadata.NormalizePackagePath(path, packageDirectory).Should().Be("templates");
        }
        else
        {
            FluentActions.Invoking(() => WorkloadMetadata.NormalizePackagePath(path, packageDirectory))
                .Should().ThrowExactly<ArgumentException>()
                .WithMessage("*inside the package root*");
        }
    }

    [Fact]
    public void NormalizePackagePath_NormalizesPackageDirectoryAndTrailingSeparators()
    {
        string packageDirectory = Path.Combine(_tempDir, "intermediate", "..") + Path.DirectorySeparatorChar;

        WorkloadMetadata.NormalizePackagePath("templates///", packageDirectory).Should().Be("templates");
    }

    [Fact]
    public void NormalizePackagePath_RejectsNullPath()
    {
        FluentActions.Invoking(() => WorkloadMetadata.NormalizePackagePath(null!, _tempDir))
            .Should().ThrowExactly<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NormalizePackagePath_RejectsInvalidPackageDirectory(string? packageDirectory)
    {
        FluentActions.Invoking(() => WorkloadMetadata.NormalizePackagePath("templates", packageDirectory!))
            .Should().Throw<ArgumentException>();
    }

    private void WriteTemplateMetadata(string kind, string? templateRootJson)
    {
        string fields = kind switch
        {
            "workload" => """, "entryPoint": { "assemblyPath": "Foo.dll", "type": "Foo.Workload" }""",
            "rid-pointer" => """, "packages": { "win-x64": "Example.win-x64" }""",
            _ => string.Empty,
        };
        string templateField = templateRootJson is null ? string.Empty : $", \"templateRoot\": {templateRootJson}";
        WriteMetadata($$"""{ "$schema": "{{SchemaUrl}}", "kind": "{{kind}}"{{fields}}{{templateField}} }""");
    }

    [Fact]
    public void Read_DefaultsKindToWorkload_WhenKindOmitted()
    {
        // workload-package-layout §5.4: kind defaults to "workload" when
        // omitted, matching the authoring default for normal workloads.
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.Kind.Should().Be(WorkloadKind.Workload);
    }

    [Fact]
    public void Read_ReturnsContentMetadata_WhenKindIsContent()
    {
        // workload-package-layout §5.4: content packages declare "kind":
        // "content" and omit entryPoint entirely.
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "content"
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.Kind.Should().Be(WorkloadKind.Content);
        metadata.EntryPoint.Should().BeNull();
    }

    [Fact]
    public void Read_ReturnsDisplayMetadata()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "content",
              "displayName": "Functions Host",
              "description": "Azure Functions host payload."
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.DisplayName.Should().Be("Functions Host");
        metadata.Description.Should().Be("Azure Functions host payload.");
    }

    [Fact]
    public void Read_ReturnsMetaMetadata_WhenKindIsMeta()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "meta"
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.Kind.Should().Be(WorkloadKind.Meta);
        metadata.EntryPoint.Should().BeNull();
    }

    [Fact]
    public void Read_ReturnsRidPointerMetadata()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "rid-pointer",
              "packages": {
                "linux-x64": "Example.Workload.linux-x64",
                "win-x64": "Example.Workload.win-x64"
              }
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.Kind.Should().Be(WorkloadKind.RidPointer);
        metadata.Packages!["win-x64"].Should().Be("Example.Workload.win-x64");
        metadata.RuntimeIdentifier.Should().BeNull();
    }

    [Fact]
    public void Read_ReturnsRidImplementationMetadata()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "content",
              "runtimeIdentifier": "linux-arm64"
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.RuntimeIdentifier.Should().Be("linux-arm64");
        metadata.Packages.Should().BeNull();
    }

    [Fact]
    public void Read_Throws_WhenRidPointerPackagesAreMissing()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "rid-pointer"
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("non-empty packages");
    }

    [Fact]
    public void Read_Throws_WhenRidPointerPackagesAreEmpty()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "rid-pointer",
              "packages": {}
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("non-empty packages");
    }

    [Fact]
    public void Read_Throws_WhenRidPointerRuntimeIdentifierIsUppercase()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "rid-pointer",
              "packages": {
                "WIN-X64": "Example.Workload.win-x64"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("WIN-X64");
        ex.Message.Should().Contain("lowercase");
    }

    [Fact]
    public void Read_Throws_WhenRidPointerHasNonLowercaseRuntimeIdentifier()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "rid-pointer",
              "packages": {
                "win-x64": "Example.Workload.win-x64",
                "WIN-X64": "Example.Workload.win-x64"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("must be lowercase");
    }

    [Fact]
    public void Read_Throws_WhenOrdinaryWorkloadDefinesPackages()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              },
              "packages": {
                "win-x64": "Example.Workload.win-x64"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("packages");
    }

    [Fact]
    public void Read_Throws_WhenRidPointerDefinesEntryPointOrRuntimeIdentifier()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "rid-pointer",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              },
              "runtimeIdentifier": "win-x64",
              "packages": {
                "win-x64": "Example.Workload.win-x64"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("entryPoint");
    }

    [Theory]
    [InlineData("content")]
    [InlineData("meta")]
    public void Read_Throws_WhenNonWorkloadKindDeclaresEntryPoint(string kind)
    {
        // workload-package-layout §5.4: entryPoint is forbidden for content
        // and meta. The author should remove it (or set kind to workload).
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "{{kind}}",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain(kind);
        ex.Message.Should().Contain("entryPoint");
    }

    [Fact]
    public void Read_Throws_WhenSchemaUnknown()
    {
        WriteMetadata(
            """
            {
              "$schema": "https://aka.ms/func-workloads/package/v999/schema.json",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("schema");
        ex.Message.Should().Contain("v999");
    }

    [Fact]
    public void Read_Throws_WhenManifestMissing()
    {
        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain(_tempDir);
        ex.Message.Should().Contain(WorkloadMetadataReader.MetadataFileName);
    }

    [Fact]
    public void Read_Throws_WhenManifestIsMalformed()
    {
        WriteMetadata("{ not valid json");

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain(WorkloadMetadataReader.MetadataFileName);
        ex.InnerException.Should().BeOfType<System.Text.Json.JsonException>();
    }

    [Fact]
    public void Read_Throws_WhenWorkloadKindMissingEntryPoint()
    {
        // kind=workload requires entryPoint.
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload"
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("entryPoint");
    }

    [Fact]
    public void Read_Throws_WhenAssemblyPathBlank()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "   ",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("assemblyPath");
    }

    [Fact]
    public void Read_Throws_WhenTypeBlank()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": ""
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("type");
    }

    [Fact]
    public void Read_Throws_WhenPackageDirectoryDoesNotExist()
    {
        string missing = Path.Combine(_tempDir, "does-not-exist");

        DirectoryNotFoundException ex = FluentActions.Invoking(() => _reader.Read(missing)).Should().ThrowExactly<DirectoryNotFoundException>().Which;

        ex.Message.Should().Contain(missing);
    }

    [Theory]
    [InlineData("/etc/passwd.dll")]
    [InlineData("/abs/path/foo.dll")]
    public void Read_Throws_WhenAssemblyPathIsAbsolute(string assemblyPath)
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "{{assemblyPath}}",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("absolute");
        ex.Message.Should().Contain("assemblyPath");
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("../../etc/passwd.dll")]
    [InlineData("nested/../../escape.dll")]
    public void Read_Throws_WhenAssemblyPathContainsParentSegment(string assemblyPath)
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "{{assemblyPath}}",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("..");
        ex.Message.Should().Contain("assemblyPath");
    }

    [Fact]
    public void Read_HydratesShippedFixtureMetadata()
    {
        // The Workloads.Tests.Fixtures.Default project ships a workload.json
        // copied next to its assembly. This test pins the contract that the
        // reader handles a real (not synthesized) fixture manifest, so a
        // breaking change to the on-disk shape (or to the fixture's manifest)
        // surfaces here.
        WorkloadMetadata metadata = _reader.Read(AppContext.BaseDirectory);

        metadata.Kind.Should().Be(WorkloadKind.Workload);
        metadata.EntryPoint!.AssemblyPath.Should().Be("Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.dll");
        metadata.EntryPoint.Type.Should().Be("Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.StubWorkload");
    }

    private void WriteMetadata(string json)
        => File.WriteAllText(
            Path.Combine(_tempDir, WorkloadMetadataReader.MetadataFileName),
            json);
}
