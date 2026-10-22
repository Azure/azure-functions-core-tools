// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Converter_ActualOwnedInputs_CompletesWithoutWaitingForStandardInput(bool keepStandardInputOpen)
    {
        string source = NodeSourceDirectory();
        string native = Path.GetFullPath(Path.Combine(source, "..", "..", "..", "..", "Stacks", "Node", "NativeTemplates"));
        string items = Path.Combine(_root, "converted-items");
        string provenance = Path.Combine(_root, "converted-provenance");
        ProcessStartInfo start = new("pwsh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
        {
            "-NoProfile", "-File", Path.Combine(native, "Convert-NodeItemTemplates.ps1"),
            "-SourceDirectory", source, "-OutputDirectory", items, "-ProvenanceDirectory", provenance,
            "-PackageId", "Azure.Functions.Templates.Node.Basic", "-PackageVersion", "1.2.3-converter-test",
        })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The owned Node converter did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        if (!keepStandardInputOpen)
        {
            process.StandardInput.Close();
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        bool completed = false;

        try
        {
            await process.WaitForExitAsync(deadline.Token);
            completed = true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // The assertion below reports a blocked producer after this test's own child has been stopped.
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        string output = await stdout.WaitAsync(TimeSpan.FromSeconds(5));
        string errors = await stderr.WaitAsync(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue(
            $"converter PID {process.Id} must exit within 20 seconds with stdin {(keepStandardInputOpen ? "open" : "closed")}; stdout: {output}; stderr: {errors}");
        process.ExitCode.Should().Be(0, errors);
        errors.Should().BeEmpty();
        Directory.GetFiles(items, "template.json", SearchOption.AllDirectories).Should().HaveCount(31);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(provenance, "compatibility.json")));
        manifest.RootElement.GetProperty("packageId").GetString().Should().Be("Azure.Functions.Templates.Node.Basic");
        manifest.RootElement.GetProperty("packageVersion").GetString().Should().Be("1.2.3-converter-test");
        manifest.RootElement.GetProperty("templates").EnumerateArray().Select(mapping => mapping.GetProperty("legacyId").GetString())
            .Should().BeEquivalentTo(ReadNodeSource().Templates.Select(template => template.Id));
        manifest.RootElement.GetProperty("projects").GetArrayLength().Should().Be(2);
        var pins = manifest.RootElement.GetProperty("sources").EnumerateArray().ToArray();
        pins.Should().HaveCount(4);
        foreach (var pin in pins)
        {
            string relative = pin.GetProperty("path").GetString()!["provenance/node/v2/".Length..];
            byte[] original = File.ReadAllBytes(Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar)));
            byte[] copied = File.ReadAllBytes(Path.Combine(provenance, "v2", relative.Replace('/', Path.DirectorySeparatorChar)));
            copied.Should().Equal(original);
            pin.GetProperty("sha256").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(original)));
        }
    }
}
