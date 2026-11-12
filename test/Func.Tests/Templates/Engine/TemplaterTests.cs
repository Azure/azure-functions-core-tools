// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

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
    public async Task ListAsync_ReturnsInstalledItemTemplate()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<ITemplateInfo> entries = await templater.ListAsync();

        entries.Should().Contain(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task ListAsync_ReturnsInstalledProjectTemplate()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<ITemplateInfo> entries = await templater.ListAsync();

        entries.Should().Contain(template => template.Identity == TemplateFixtures.ProjectTemplateIdentity);
    }

    [Fact]
    public async Task CreateAsync_UsesExactInstalledTemplateAndEngineDefaultsWithoutForce()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteHttpItemPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        IReadOnlyList<ITemplateInfo> inventory = await templater.ListAsync();
        ITemplateInfo selected = inventory.Should()
            .ContainSingle(template => template.Identity == TemplateFixtures.HttpItemTemplateIdentity).Subject;
        string outputPath = Path.Combine(_root, "generated");
        IReadOnlyDictionary<string, string?> inputs = new Dictionary<string, string?>
        {
            ["AccessRights"] = "Anonymous",
        };

        ITemplateCreationResult created = await templater.CreateAsync(
            selected,
            "SampleHttp",
            null,
            outputPath,
            inputs,
            CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success);
        created.CreationResult.Should().NotBeNull();
        string generatedFile = Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        string content = await File.ReadAllTextAsync(generatedFile);
        content.Should().Contain("HttpTriggerCSharp");
        content.Should().Contain("Anonymous");
        content.Should().Contain("Company.Function", "unspecified parameters retain the Template Engine template default");

        string conflictPath = Path.Combine(_root, "conflict");
        string conflictingFile = Path.Combine(conflictPath, Path.GetRelativePath(outputPath, generatedFile));
        Directory.CreateDirectory(Path.GetDirectoryName(conflictingFile)!);
        await File.WriteAllTextAsync(conflictingFile, "preserve existing file");
        Func<Task> createCollision = () => templater.CreateAsync(
            selected,
            "SampleHttp",
            null,
            conflictPath,
            inputs,
            CancellationToken.None);

        await createCollision.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The template would overwrite existing files.");
        (await File.ReadAllTextAsync(conflictingFile)).Should().Be("preserve existing file", "creation is performed with force disabled");
    }

    [Fact]
    public async Task ListAsync_ReturnsTemplatesWithoutFuncTypeMetadata()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);

        // The engine installs both, so only their type keeps them out of the catalog.
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.OtherTypeTemplateIdentity);
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.UntypedTemplateIdentity);
        using Templater templater = CreateTemplater();

        IReadOnlyList<ITemplateInfo> templates = await templater.ListAsync();

        templates.Select(template => template.Identity).Should().BeEquivalentTo(
            [
                TemplateFixtures.ItemTemplateIdentity,
                TemplateFixtures.ProjectTemplateIdentity,
                TemplateFixtures.OtherTypeTemplateIdentity,
                TemplateFixtures.UntypedTemplateIdentity,
            ]);
    }

    [Fact]
    public async Task ListAsync_FromEngineCache_ReturnsSameInventoryAsFromScan()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), _hive);
        IReadOnlyList<ITemplateInfo> scanned = await ListWithNewTemplaterAsync();

        IReadOnlyList<ITemplateInfo> cached = await ListWithNewTemplaterAsync();

        scanned.Select(entry => entry.Identity).Should().BeEquivalentTo(
        [
            TemplateFixtures.MetadataTemplateIdentity,
            TemplateFixtures.InvalidHostTemplateIdentity,
            TemplateFixtures.DotnetHostOnlyTemplateIdentity,
            TemplateFixtures.HiddenTemplateIdentity,
        ]);
        cached.Select(entry => entry.Identity).Should().BeEquivalentTo(scanned.Select(entry => entry.Identity));
    }

    [Fact]
    public async Task ListAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using Templater templater = CreateTemplater();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => templater.ListAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CreateAsync_CanceledTokenDoesNotCreateFiles()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteHttpItemPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        ITemplateInfo selected = (await templater.ListAsync())
            .Should().ContainSingle(template => template.Identity == TemplateFixtures.HttpItemTemplateIdentity).Subject;
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => templater.CreateAsync(
            selected,
            "SampleHttp",
            null,
            Path.Combine(_root, "generated"),
            new Dictionary<string, string?>(),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(_root, "generated")).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.ListAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_NullSession_Throws()
    {
        Action act = () => _ = new Templater(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    private async Task<IReadOnlyList<ITemplateInfo>> ListWithNewTemplaterAsync()
    {
        using Templater templater = CreateTemplater();
        return await templater.ListAsync();
    }

    private Templater CreateTemplater()
        => new(new TemplateEngineSession(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive));
}
