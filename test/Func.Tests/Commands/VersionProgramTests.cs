// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Commands;

public class VersionProgramTests(ITestOutputHelper output)
{
    private readonly CliProcessTestRunner _runner = new(output);

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("--version", false)]
    [InlineData("version", false)]
    [InlineData("--verbose", true)]
    public async Task Program_VersionInspection_PreservesVersionAndBuildDetailsOnRepeat(string command, bool detailed)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-version-{Guid.NewGuid():N}");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workload-home"));
        string project = Path.Combine(directory, "project");
        string informationalVersion = typeof(VersionCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        string version = informationalVersion.Split('+')[0];
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        try
        {
            Directory.CreateDirectory(paths.Home);
            Directory.CreateDirectory(project);
            await File.WriteAllTextAsync(paths.VersionCachePath, "0.0.0", cancellation.Token);

            CliProcessTestRunner.CliResult first = await _runner.RunAsync(directory, paths.Home, workloads.Home, project, [command], cancellation.Token);
            CliProcessTestRunner.CliResult repeated = await _runner.RunAsync(directory, paths.Home, workloads.Home, project, [command], cancellation.Token);
            CliProcessTestRunner.CliResult[] results = [first, repeated];

            foreach (CliProcessTestRunner.CliResult result in results)
            {
                result.ExitCode.Should().Be(0);
                result.StandardError.Should().BeEmpty();
                if (detailed)
                {
                    ReadVersionProperty(result.StandardOutput, "Version", "Build").Should().Be(version).And.NotContain("+");
                    ReadVersionProperty(result.StandardOutput, "Build", "Runtime").Should().Be(informationalVersion);
                }
                else
                {
                    result.StandardOutput.Should().Be(version + Environment.NewLine).And.NotContain("+");
                }
            }

            File.Exists(Path.Combine(paths.Home, FileFirstRunStateStore.MarkerFileName)).Should().BeFalse();
            File.Exists(workloads.WorkloadRegistryPath).Should().BeFalse();
            (await File.ReadAllTextAsync(paths.VersionCachePath, cancellation.Token)).Should().Be("0.0.0");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("|", false)]
    [InlineData("\u2502", false)]
    [InlineData("\u0393\u00f6\u00e9", true)]
    public void ReadVersionProperty_ReconstructsExactValuesAcrossWrappedRows(string border, bool unexpectedMetadata)
    {
        string version = unexpectedMetadata ? "5.0.0-preview.5+unexpected" : "5.0.0-preview.5";
        string rendered = string.Join(
            Environment.NewLine,
            $"{border} Version {border} {version} {border}",
            "+-----------------+-----------------+",
            $"{border} Build {border} 5.0.0-preview.5 {border}",
            $"{border} {border} +0123456789abcdef0123 {border}",
            $"{border} {border} 456789abcdef01234567 {border}",
            $"{border} Runtime {border} .NET 10.0.0 {border}");

        string actualVersion = ReadVersionProperty(rendered, "Version", "Build");
        string actualBuild = ReadVersionProperty(rendered, "Build", "Runtime");

        actualVersion.Should().Be(version);
        actualBuild.Should().Be("5.0.0-preview.5+0123456789abcdef0123456789abcdef01234567");
    }

    private static string ReadVersionProperty(string output, string property, string nextProperty)
    {
        var labels = Regex.Matches(output, $@"(?<=\s){property}(?=\s)");
        var nextLabels = Regex.Matches(output, $@"(?<=\s){nextProperty}(?=\s)");
        labels.Count.Should().Be(1);
        nextLabels.Count.Should().Be(1);
        int start = labels[0].Index + labels[0].Length;
        int end = nextLabels[0].Index;
        end.Should().BeGreaterThan(start);

        // Version values stay ASCII even when redirected table borders decode differently.
        return string.Concat(Regex.Matches(output[start..end], @"[.+-]*[A-Za-z0-9][A-Za-z0-9.+-]*")
            .Select(match => match.Value));
    }
}
