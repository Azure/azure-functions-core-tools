// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Tests.Templates;

public class TemplatesWorkloadManifestReaderTests : IDisposable
{
    private readonly string _installDir;

    public TemplatesWorkloadManifestReaderTests()
    {
        _installDir = Path.Combine(Path.GetTempPath(), "func-new-pr4-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_installDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Missing_File_Returns_Null()
    {
        TemplatesWorkloadManifestReader.GetMinBundleVersion(_installDir).Should().BeNull();
    }

    [Fact]
    public void Reads_MinBundleVersion()
    {
        string contentDir = Path.Combine(_installDir, "tools", "any", "content");
        Directory.CreateDirectory(contentDir);
        File.WriteAllText(
            Path.Combine(contentDir, "templates-workload.json"),
            """{ "minBundleVersion": "[4.0.0, )" }""");

        TemplatesWorkloadManifestReader.GetMinBundleVersion(_installDir).Should().Be("[4.0.0, )");
    }

    [Fact]
    public void Missing_Key_Returns_Null()
    {
        string contentDir = Path.Combine(_installDir, "tools", "any", "content");
        Directory.CreateDirectory(contentDir);
        File.WriteAllText(
            Path.Combine(contentDir, "templates-workload.json"),
            """{ "somethingElse": "ignored" }""");

        TemplatesWorkloadManifestReader.GetMinBundleVersion(_installDir).Should().BeNull();
    }

    [Fact]
    public void Malformed_Json_Returns_Null()
    {
        string contentDir = Path.Combine(_installDir, "tools", "any", "content");
        Directory.CreateDirectory(contentDir);
        File.WriteAllText(Path.Combine(contentDir, "templates-workload.json"), "{ not json");

        TemplatesWorkloadManifestReader.GetMinBundleVersion(_installDir).Should().BeNull();
    }
}
