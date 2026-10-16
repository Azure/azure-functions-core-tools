// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public sealed class PythonTemplateAppendExecutorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-python-append-").FullName;
    private readonly IPythonTemplateAppendExecutor _executor = new PythonTemplateAppendExecutor();

    private string App => Path.Combine(_root, "function_app.py");

    [Theory]
    [InlineData("")]
    [InlineData("import logging\n")]
    [InlineData("import logging\r\n")]
    [InlineData("import logging")]
    [InlineData("import logging\r")]
    public async Task AppendAsync_ApprovedTarget_PreservesAllBytesAndLegacyNewlineRule(string original)
    {
        byte[] originalBytes = Encoding.UTF8.GetBytes(original);
        File.WriteAllBytes(App, originalBytes);
        string digest = Convert.ToHexString(SHA256.HashData(originalBytes));
        const string snippet = "@app.route(route='new')\ndef greeting(req):\n    return func.HttpResponse('hello')";

        await _executor.AppendAsync(App, snippet, digest, CancellationToken.None);

        string separator = original.Length == 0 || original.EndsWith('\n') ? string.Empty : Environment.NewLine;
        File.ReadAllBytes(App).Should().Equal([.. originalBytes, .. Encoding.UTF8.GetBytes(separator + snippet)]);
    }

    [Fact]
    public async Task AppendAsync_Utf8BomAndUserFunctions_PreservesOriginalEncodingPrefixAndCode()
    {
        const string original = "import json\nimport azure.functions as func\napp = func.FunctionApp()\n@app.route(route='old')\ndef old(req):\n    return '\u00e9'";
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(original)];
        File.WriteAllBytes(App, bytes);
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));

        await _executor.AppendAsync(App, "\n@app.route(route='new')\ndef greeting(req):\n    return 'new'", digest, CancellationToken.None);

        File.ReadAllBytes(App).Take(bytes.Length).Should().Equal(bytes);
        File.ReadAllText(App).Should().Contain("def old(req):", "def greeting(req):");
    }

    [Fact]
    public async Task AppendAsync_TargetChangedAfterPreview_RefusesWithoutMutation()
    {
        File.WriteAllText(App, "approved");
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(App)));
        File.WriteAllText(App, "new user edits");

        Func<Task> append = () => _executor.AppendAsync(App, "snippet", digest, CancellationToken.None);

        await append.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed after preview*");
        File.ReadAllText(App).Should().Be("new user edits");
    }

    [Fact]
    public async Task AppendAsync_RepeatedApprovedEffect_RefusesRatherThanDuplicatingFunction()
    {
        File.WriteAllText(App, "user code\n");
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(App)));
        await _executor.AppendAsync(App, "snippet", digest, CancellationToken.None);

        Func<Task> repeat = () => _executor.AppendAsync(App, "snippet", digest, CancellationToken.None);

        await repeat.Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllText(App).Should().Be("user code\nsnippet");
    }

    [Fact]
    public async Task AppendAsync_TargetMissing_RefusesWithoutCreatingAppOrParent()
    {
        Func<Task> append = () => _executor.AppendAsync(App, "snippet", new string('0', 64), CancellationToken.None);

        await append.Should().ThrowAsync<FileNotFoundException>();
        File.Exists(App).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_ParentMissing_RefusesWithoutCreatingDirectory()
    {
        string target = Path.Combine(_root, "missing", "function_app.py");
        Func<Task> append = () => _executor.AppendAsync(target, "snippet", new string('0', 64), CancellationToken.None);

        await append.Should().ThrowAsync<DirectoryNotFoundException>();
        Directory.Exists(Path.GetDirectoryName(target)).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_PreCanceled_LeavesApprovedTargetUntouched()
    {
        File.WriteAllText(App, "user code");
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(App)));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> append = () => _executor.AppendAsync(App, "snippet", digest, cancellation.Token);

        await append.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(App).Should().Be("user code");
    }

    [Theory]
    [InlineData(null, "snippet", null)]
    [InlineData("", "snippet", null)]
    [InlineData("relative.py", "snippet", null)]
    [InlineData("app", null, null)]
    [InlineData("app", "", null)]
    [InlineData("app", " ", null)]
    [InlineData("app", "snippet", "bad")]
    [InlineData("app", "snippet", "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task AppendAsync_InvalidContract_RefusesBeforeFileEffects(string? path, string? snippet, string? digest)
    {
        string? target = path == "app" ? App : path;
        File.WriteAllText(App, "user code");
        digest ??= Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(App)));

        Func<Task> append = () => _executor.AppendAsync(target!, snippet!, digest, CancellationToken.None);

        await append.Should().ThrowAsync<ArgumentException>();
        File.ReadAllText(App).Should().Be("user code");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
