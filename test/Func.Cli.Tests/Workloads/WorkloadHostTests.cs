// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

[Collection(nameof(WorkingDirectoryTests))]
public class WorkloadHostTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void DiscoverWorkloads_EmptyRoot_ReturnsEmpty()
    {
        var (host, _, _) = WorkloadTestFactory.Create(_interaction);
        Assert.Empty(host.DiscoverWorkloads());
    }

    [Fact]
    public void DiscoverWorkloads_SkipsManifestWithMismatchedProtocol()
    {
        var (host, _, root) = WorkloadTestFactory.Create(_interaction);
        var dir = Path.Combine(root, "broken", "1.0.0");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "workload.json"), """
            { "id":"broken", "version":"1.0.0", "protocolVersion":"99.0",
              "executable":"x", "workerRuntimes":["x"] }
            """);

        Assert.Empty(host.DiscoverWorkloads());
        Assert.Contains(_interaction.Lines, l => l.Contains("WARNING") && l.Contains("broken"));
    }

    [Fact]
    public async Task SampleWorkload_RoundTrip_ProjectInitAndTemplatesAndPack()
    {
        // Skip if we can't locate the sample workload build output (e.g., before
        // the workload project has been built). The build will produce it for CI runs.
        var sampleDir = LocateSampleBuildOutput();
        if (sampleDir is null)
        {
            return;
        }

        var (host, installer, _) = WorkloadTestFactory.Create(_interaction);

        await installer.InstallAsync("sample", sampleDir);
        Assert.NotEmpty(installer.GetInstalled());

        var discovered = host.DiscoverWorkloads();
        Assert.Single(discovered);
        Assert.Equal("sample", discovered[0].Manifest.Id);

        await using var client = await host.StartByIdAsync("sample");
        Assert.NotNull(client.InitializeResult);
        Assert.Equal("sample", client.InitializeResult!.WorkloadId);

        // project.init
        var workDir = Path.Combine(Path.GetTempPath(), "oop-test-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var initResult = await client.InvokeAsync(
                WorkloadProtocol.Methods.ProjectInit,
                new ProjectInitParams(workDir, "sample", "Demo", "MyApp", false, null),
                WorkloadJsonContext.Default.ProjectInitParams,
                WorkloadJsonContext.Default.ProjectInitResult);
            Assert.NotEmpty(initResult.FilesCreated);
            Assert.True(File.Exists(Path.Combine(workDir, "host.json")));

            // templates.list
            var listResult = await client.InvokeAsync(
                WorkloadProtocol.Methods.TemplatesList,
                new TemplatesListParams(null),
                WorkloadJsonContext.Default.TemplatesListParams,
                WorkloadJsonContext.Default.TemplatesListResult);
            Assert.Contains(listResult.Templates, t => t.Name == "HttpTrigger");

            // templates.create
            var createResult = await client.InvokeAsync(
                WorkloadProtocol.Methods.TemplatesCreate,
                new TemplatesCreateParams("HttpTrigger", "Hello", workDir, null, null, false),
                WorkloadJsonContext.Default.TemplatesCreateParams,
                WorkloadJsonContext.Default.TemplatesCreateResult);
            Assert.Single(createResult.FilesCreated);
            Assert.True(File.Exists(createResult.FilesCreated[0]));

            // pack.run
            var packResult = await client.InvokeAsync(
                WorkloadProtocol.Methods.PackRun,
                new PackRunParams(workDir, null, false),
                WorkloadJsonContext.Default.PackRunParams,
                WorkloadJsonContext.Default.PackRunResult);
            Assert.True(File.Exists(packResult.OutputPath));
        }
        finally
        {
            Directory.Delete(workDir, true);
        }
    }

    private static string? LocateSampleBuildOutput()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var probe = Path.Combine(current, "src", "Func.Workload.Sample", "bin");
            if (Directory.Exists(probe))
            {
                return Directory.EnumerateFiles(probe, "workload.json", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Select(Path.GetDirectoryName)
                    .FirstOrDefault();
            }
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar)) ?? current;
        }
        return null;
    }
}
