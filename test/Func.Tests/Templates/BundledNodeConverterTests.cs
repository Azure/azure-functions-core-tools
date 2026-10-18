// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("lineEndings", true)]
    [InlineData("whitespace", false)]
    [InlineData("comments", false)]
    [InlineData("changedLiteral", false)]
    [InlineData("ambiguousDeclaration", false)]
    [InlineData("changedAssignment", false)]
    [InlineData("queueBlockComment", false)]
    [InlineData("timerTemplateLiteral", false)]
    [InlineData("queueUnrelatedObject", false)]
    public async Task Converter_ChangedSource_AssociatesOnlyReviewedDeclarationsOrFailsClosed(string change, bool succeeds)
    {
        string owned = NodeSourceDirectory();
        string source = Path.Combine(_root, "converter-input");
        foreach (string relative in new[] { "templates/templates.json", "templates/_bindings.json", "bindings/userPrompts.json", "resources/Resources.json" })
        {
            string target = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(owned, relative.Replace('/', Path.DirectorySeparatorChar)), target);
        }
        string catalog = Path.Combine(source, "templates", "templates.json");
        var templates = JsonNode.Parse(File.ReadAllBytes(catalog))!.AsArray();
        const string decoys = "\n// schedule: '0 */5 * * * *',\nconst unrelated = '0 */5 * * * *';\n";
        foreach (string language in new[] { "JavaScript", "TypeScript" })
        {
            string kind = change.StartsWith("queue", StringComparison.Ordinal) ? "QueueTrigger" : "TimerTrigger";
            var raw = templates.Single(template => template!["id"]!.GetValue<string>() == kind + "-" + language)!;
            string file = language == "JavaScript" ? "function.js" : "function.ts";
            string body = raw["files"]![file]!.GetValue<string>();
            if (change == "unchanged") continue;
            if (change == "lineEndings") raw["files"]![file] = body.Replace("\r\n", "\n", StringComparison.Ordinal);
            else if (change == "whitespace") raw["files"]![file] = body + " ";
            else if (change == "comments") raw["files"]![file] = body + decoys;
            else if (change == "changedLiteral") raw["files"]![file] = body.Replace("schedule: '0 */5 * * * *'", "schedule: 'changed'");
            else if (change == "ambiguousDeclaration") raw["files"]![file] = body + "\n    schedule: '0 */5 * * * *',\n";
            else if (change == "changedAssignment") raw["jobs"]![0]!["inputs"]![1]!["assignTo"] = "$(OTHER_INPUT)";
            else if (change == "queueBlockComment") raw["files"]![file] = body.Replace("connection: ''", "connection: 'changed'")
                + "\n/*\n    connection: '',\n*/\n";
            else if (change == "timerTemplateLiteral") raw["files"]![file] = body.Replace("schedule: '0 */5 * * * *'", "schedule: '0 */9 * * * *'")
                + "\nconst decoy = `\n    schedule: '0 */5 * * * *',\n`;\n";
            else raw["files"]![file] = body.Replace("connection: ''", "connection: 'changed'")
                + "\nconst decoy = {\n    connection: '',\n};\n";
        }
        File.WriteAllText(catalog, templates.ToJsonString());
        if (change.StartsWith("queue", StringComparison.Ordinal) || change == "timerTemplateLiteral")
        {
            string kind = change.StartsWith("queue", StringComparison.Ordinal) ? "QueueTrigger" : "TimerTrigger";
            string validSource = templates.Single(template => template!["id"]!.GetValue<string>() == kind + "-JavaScript")!
                ["files"]!["function.js"]!.GetValue<string>();
            string validPath = Path.Combine(_root, "valid-decoy.js");
            File.WriteAllText(validPath, validSource);
            (await EvaluateNodePropertyAsync(validPath)).Should().Be(change == "timerTemplateLiteral" ? "\"0 */9 * * * *\"" : "\"changed\"");
        }
        string items = Path.Combine(_root, "converted-items");
        string provenance = Path.Combine(_root, "converted-provenance");
        string native = Path.GetFullPath(Path.Combine(owned, "..", "..", "..", "..", "Stacks", "Node", "NativeTemplates"));
        ProcessStartInfo start = new("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(native, "Convert-NodeItemTemplates.ps1"),
            "-SourceDirectory", source, "-OutputDirectory", items, "-ProvenanceDirectory", provenance,
            "-PackageId", "Azure.Functions.Templates.Node.Basic", "-PackageVersion", "1.2.3-guard-test" })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        string errors = await stderr;
        (await stdout).Should().BeEmpty();
        if (!succeeds && process.ExitCode == 0 && (change.StartsWith("queue", StringComparison.Ordinal) || change == "timerTemplateLiteral"))
        {
            string kind = change.StartsWith("queue", StringComparison.Ordinal) ? "QueueTrigger" : "TimerTrigger";
            string generated = Path.Combine(items, kind + "-JavaScript", "funcNodeItem.js");
            (await EvaluateNodePropertyAsync(generated)).Should().Be(change == "timerTemplateLiteral" ? "\"0 */9 * * * *\"" : "\"changed\"");
        }
        process.ExitCode.Should().Be(succeeds ? 0 : 1, errors);
        if (succeeds)
        {
            foreach (string language in new[] { "JavaScript", "TypeScript" })
            {
                string extension = language == "JavaScript" ? ".js" : ".ts";
                File.ReadAllText(Path.Combine(items, "TimerTrigger-" + language, "funcNodeItem" + extension)).Should()
                    .Contain("schedule: '__FUNC_NODE_timerTrigger-schedule__',");
            }
        }
        else
        {
            errors.Should().Contain(change == "changedAssignment" ? "property association" : "declaration");
        }
    }

    private static async Task<string> EvaluateNodePropertyAsync(string path)
    {
        ProcessStartInfo start = new("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("""
            const fs = require('fs'), vm = require('vm');
            let value;
            const capture = (_, options) => value = options.connection ?? options.schedule;
            vm.runInNewContext(fs.readFileSync(process.argv[1], 'utf8'), {
              require: () => ({app: {storageQueue: capture, timer: capture}})
            });
            process.stdout.write(JSON.stringify(value));
            """);
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        process.ExitCode.Should().Be(0, await stderr);
        return await stdout;
    }

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
