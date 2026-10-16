// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class DotNetDirectProviderTests
{
    [Theory]
    [InlineData("unpinned")]
    [InlineData("unknownAction")]
    [InlineData("layout")]
    public async Task ItemProvider_UnpinnedNativeConfiguration_RefusesBeforeAnyEffects(string corruption)
    {
        using var fixture = await OpenAsync("F#", corruption);
        var selected = await SelectItemAsync(fixture, "F#");
        var before = File.ReadAllBytes(fixture.Project!);

        var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, Parse(selected, []), "Refused"), _deadline.Token);

        prepared.Request.Should().BeNull();
        prepared.Diagnostics.Should().ContainSingle().Which.Should().Contain("not a pinned native source");
        File.ReadAllBytes(fixture.Project!).Should().Equal(before);
        File.Exists(Path.Combine(fixture.Directory.Info.FullName, "Refused.fs")).Should().BeFalse();
        Directory.Exists(Path.Combine(fixture.Directory.Info.FullName, "nested")).Should().BeFalse();
        fixture.Runner.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ItemProvider_ConfigurationChangedAfterSelection_RefusesBeforeGeneration()
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        var installedArchive = selected.ConfigurationSourceIdentity!.Split("::", StringSplitOptions.None)[0];
        File.Exists(installedArchive).Should().BeTrue();
        using (var archive = ZipFile.Open(installedArchive, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry(fixture.ItemConfiguration)!;
            string content;
            using (StreamReader reader = new(entry.Open()))
            {
                content = reader.ReadToEnd();
            }
            entry.Delete();
            using StreamWriter writer = new(archive.CreateEntry(fixture.ItemConfiguration).Open());
            writer.Write(content + "\n ");
        }

        var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, Parse(selected, []), "Refused"), _deadline.Token);

        prepared.Request.Should().BeNull();
        string.Join("; ", prepared.Diagnostics).Should().Contain("changed");
        File.Exists(Path.Combine(fixture.Directory.Info.FullName, "Refused.fs")).Should().BeFalse();
        fixture.Runner.Commands.Should().BeEmpty();
    }

    [SymbolicLinkFact]
    public async Task ItemProvider_LinkedSelectedProject_RefusesAndPreservesExternalBytes()
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        var outside = Path.Combine(_root, "external.fsproj");
        File.Move(fixture.Project!, outside);
        var before = File.ReadAllBytes(outside);
        File.CreateSymbolicLink(fixture.Project!, outside);

        var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, Parse(selected, []), "Refused"), _deadline.Token);

        prepared.Request.Should().BeNull();
        string.Join("; ", prepared.Diagnostics).Should().Contain("refuses linked or occupied file paths");
        File.ReadAllBytes(outside).Should().Equal(before);
        File.Exists(Path.Combine(fixture.Directory.Info.FullName, "Refused.fs")).Should().BeFalse();
        fixture.Runner.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Provider_NullAndPreCanceledContracts_RefuseBeforeEffects()
    {
        using var fixture = await OpenAsync();
        Func<Task> nullItem = () => fixture.Provider.PrepareAsync((NativeItemWorkflowContext)null!, _deadline.Token);
        Func<Task> nullProject = () => fixture.Provider.PrepareAsync((NativeProjectWorkflowContext)null!, _deadline.Token);
        Func<Task> nullSource = () => fixture.Provider.QualifyLayoutAsync(null!, _deadline.Token);
        Action nullEntry = () => fixture.Provider.Handles(null!);
        await nullItem.Should().ThrowAsync<ArgumentNullException>();
        await nullProject.Should().ThrowAsync<ArgumentNullException>();
        await nullSource.Should().ThrowAsync<ArgumentNullException>();
        nullEntry.Should().Throw<ArgumentNullException>();
        var selected = (await fixture.Templater.ResolveGroupAsync("func", TemplateType.Project, _deadline.Token)).Group![0];
        var context = ItemContext(fixture, selected, Parse(selected, []), "Refused");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> canceled = () => fixture.Provider.PrepareAsync(context, cancellation.Token);

        await canceled.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(fixture.Directory.Info.FullName).Should().BeFalse();
        fixture.Runner.Commands.Should().BeEmpty();
    }
}
