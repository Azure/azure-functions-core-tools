// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadSourceErrorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InstallAndUpdate_InvalidCatalogSource_IsAGracefulUserError(bool update, bool exact)
    {
        var interaction = new TestInteractionService();
        var installer = Substitute.For<IWorkloadInstaller>();
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var options = Options.Create(new WorkloadCatalogOptions());
        WorkloadCatalog catalog = new(options, new PackageSourceProvider(options), _ => throw new InvalidOperationException("Unexpected client"));
        installer.InstallFromCatalogAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (call.ArgAt<bool>(4))
                    await catalog.ResolveLatestVersionAsync("demo", false, source: call.ArgAt<string?>(2));
                else
                    await catalog.SearchAsync(new CatalogSearchQuery { Source = call.ArgAt<string?>(2) });
                return await Task.FromException<WorkloadInstallResult>(new InvalidOperationException("Invalid source was accepted"));
            });
        installer.UpdateAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await catalog.ResolveLatestVersionAsync("demo", false, source: call.ArgAt<string?>(2));
                return await Task.FromException<WorkloadUpdateResult>(new InvalidOperationException("Invalid source was accepted"));
            });
        var updateCommand = new WorkloadUpdateCommand(interaction, installer, store, options);
        Command command = update ? updateCommand : new WorkloadInstallCommand(interaction, installer, store, updateCommand, options);
        string[] arguments = exact ? ["demo", "--source", "./feed", "--exact"] : ["demo", "--source", "./feed"];

        var failure = await FluentActions.Awaiting(() => command.Parse(arguments).InvokeAsync(
                new InvocationConfiguration { EnableDefaultExceptionHandler = false }))
            .Should().ThrowAsync<GracefulException>();

        failure.Which.IsUserError.Should().BeTrue();
        failure.Which.Message.Should().Contain("not a supported NuGet feed");
        failure.Which.InnerException.Should().BeOfType<InvalidWorkloadSourceException>();
    }

    [Fact]
    public async Task UpdateAll_InvalidSource_ReportsEachFailureAndContinues()
    {
        var interaction = new TestInteractionService();
        var installer = Substitute.For<IWorkloadInstaller>();
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([
            new WorkloadEntry { PackageId = "first", PackageVersion = "1.0.0" },
            new WorkloadEntry { PackageId = "second", PackageVersion = "1.0.0" },
        ]);
        var options = Options.Create(new WorkloadCatalogOptions());
        WorkloadCatalog catalog = new(options, new PackageSourceProvider(options), _ => throw new InvalidOperationException("Unexpected client"));
        installer.UpdateAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await catalog.ResolveLatestVersionAsync(call.ArgAt<string>(0), false, source: call.ArgAt<string?>(2));
                return await Task.FromException<WorkloadUpdateResult>(new InvalidOperationException("Invalid source was accepted"));
            });
        var command = new WorkloadUpdateCommand(interaction, installer, store, options);

        int exit = await command.Parse(["--all", "--source", "./feed"]).InvokeAsync(
            new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exit.Should().Be(1);
        interaction.AllOutput.Should().Contain("Update failed for 'first'").And.Contain("Update failed for 'second'")
            .And.Contain("not a supported NuGet feed");
        await installer.Received(2).UpdateAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), "./feed", Arg.Any<bool?>(),
            Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }
}