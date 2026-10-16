// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetTemplateSourcesTests
{
    [Fact]
    public void SourceQualification_ActualNativeArchive_MatchesAllPinnedConfigurationBytes()
    {
        string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        using ZipArchive outer = ZipFile.OpenRead(archive);
        using var bytes = new MemoryStream();
        using (Stream content = outer.GetEntry("tools/any/templates/dotnet.nupkg")!.Open())
        {
            content.CopyTo(bytes);
        }
        bytes.Position = 0;
        using ZipArchive inner = new(bytes);
        DotNetTemplateSources sources = new();
        ZipArchiveEntry[] configurations = [.. inner.Entries.Where(entry => entry.FullName.EndsWith("/.template.config/template.json", StringComparison.Ordinal))];
        configurations.Should().HaveCount(38);
        foreach (ZipArchiveEntry entry in configurations)
        {
            using Stream stream = entry.Open();
            using var configBytes = new MemoryStream();
            stream.CopyTo(configBytes);
            using var configuration = JsonDocument.Parse(configBytes.ToArray());
            JsonElement root = configuration.RootElement;
            TemplateCatalogEntry catalog = new(root.GetProperty("identity").GetString()!, root.GetProperty("name").GetString()!, null,
                root.GetProperty("tags").GetProperty("type").GetString() == "project" ? TemplateType.Project : TemplateType.Item,
                [], null, root.GetProperty("tags").GetProperty("language").GetString(), 0,
                new("Azure.Functions.Templates.DotNet", "1.0.0"), false, [], null);
            string hash = Convert.ToHexString(SHA256.HashData(configBytes.ToArray()));

            sources.Matches(catalog, hash).Should().BeTrue(entry.FullName);
            sources.Matches(catalog, new string('0', 64)).Should().BeFalse();
            sources.Matches(catalog with { Package = new("Unqualified.Package", "1.0.0") }, hash).Should().BeFalse();
        }
    }
}
