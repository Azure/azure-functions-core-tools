// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterTests : IDisposable
{
    private readonly string _root;
    private readonly string _hive;

    public TemplaterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-templater-tests-" + Guid.NewGuid().ToString("N"));
        _hive = Path.Combine(_root, "hive");
        Directory.CreateDirectory(_hive);
    }

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
    public async Task GetTemplatesAsync_ReturnsTemplatesInstalledInHive()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<ITemplateInfo> templates = await templater.GetTemplatesAsync(CancellationToken.None);

        templates.Should().HaveCount(2);
        templates.Should().ContainSingle(template => template.Identity == TemplateFixtures.ItemTemplateIdentity)
            .Which.TagsCollection["type"].Should().Be("item");
        templates.Should().ContainSingle(template => template.Identity == TemplateFixtures.ProjectTemplateIdentity)
            .Which.TagsCollection["type"].Should().Be("project");
    }

    [Fact]
    public async Task GetTemplatesAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.GetTemplatesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_NullSession_Throws()
    {
        Action act = () => _ = new Templater(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    private Templater CreateTemplater()
        => new(new TemplateEngineSession(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive));
}
