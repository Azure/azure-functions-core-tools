// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagementPreclassification_ExternalWorkloadLiteral_PreservesConfigurationAndOneConfigureRegistration(bool response)
    {
        WorkloadRuntimeIdentifierProvider runtime = new();
        var installer = CreateInstaller(new FileWorkloadOperationCoordinator(_paths), runtimeOverride: runtime);
        await installer.InstallFromPackageAsync(UnrelatedRuntimeArchive("1.0.0", "management-probe", runtime.Current, "ConfigurationProbeWorkload"));
        string project = Path.Combine(_root, "management-config-project");
        Directory.CreateDirectory(Path.Combine(project, ".func"));
        string marker = Path.Combine(_root, "configure-passes.txt");
        File.WriteAllText(Path.Combine(project, ".func", "config.json"), JsonSerializer.Serialize(new
        {
            review = new { value = "project", named = "project", direct = "project", configureMarker = marker },
        }));
        string[] arguments = ["configuration-probe", "workload", "--value", "workload"];
        if (response)
        {
            string file = Path.Combine(_root, "management-runtime.rsp");
            File.WriteAllText(file, "configuration-probe workload --value workload");
            arguments = ["@" + file];
        }
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "management-runtime-process"), Home, _paths.Home,
            project, arguments, timeout.Token);

        result.ExitCode.Should().Be(73, result.StandardOutput + result.StandardError);
        result.StandardError.Should().BeEmpty();
        File.ReadAllLines(marker).Should().Equal("project");
    }

    [Fact]
    public async Task ManagementPreclassification_MalformedCwdConfiguration_RemainsTheOriginalFailureBeforeMutation()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "malformed-management-cwd");
        Directory.CreateDirectory(Path.Combine(project, ".func"));
        File.WriteAllText(Path.Combine(project, ".func", "config.json"), "{malformed configuration");
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "malformed-management-process"), Home, _paths.Home, project,
            ["workload", "uninstall", "Azure.Functions.Cli.Workloads.Node", "--exact"], timeout.Token);

        result.ExitCode.Should().Be(1, result.StandardOutput + result.StandardError);
        result.StandardError.Should().Contain("config").And.NotContain("could not be deleted");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        AssertMigrationFilesUnchanged(Path.Combine(Home, "templates"), native);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }
}
