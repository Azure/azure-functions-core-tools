// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Native;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public sealed class PythonTemplatePolicyTests
{
    private readonly PythonTemplatePolicy _policy = new(new(), Substitute.For<IPythonTemplateAppendExecutor>(), new());

    [Theory]
    [InlineData("Azure.Functions.Templates.Python", "Python", false, "Azure.Functions.Python.HttpTrigger", true)]
    [InlineData("Azure.Functions.Templates.Python", "Python", true, "Azure.Functions.Python.Basic", true)]
    [InlineData("Azure.Functions.Templates.Other", "Python", false, "Azure.Functions.Python.HttpTrigger", false)]
    [InlineData("Azure.Functions.Templates.Python", "JavaScript", false, "Azure.Functions.Python.HttpTrigger", false)]
    [InlineData("Azure.Functions.Templates.Python", "Python", true, "Another.Project", false)]
    public void Handles_SelectedMetadata_ClaimsOnlyBundledPythonItemsOrBasic(
        string packageId, string language, bool project, string identity, bool expected)
    {
        NativeTemplateSource source = Substitute.For<NativeTemplateSource>();
        source.PackageId.Returns(packageId);
        source.Language.Returns(language);
        source.IsProject.Returns(project);
        source.Identity.Returns(identity);

        _policy.Handles(source).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, "invalidData")]
    [InlineData(false, "io")]
    [InlineData(false, "access")]
    [InlineData(false, "json")]
    [InlineData(true, "invalidData")]
    [InlineData(true, "io")]
    [InlineData(true, "access")]
    [InlineData(true, "json")]
    public async Task Qualify_UnreadableSource_PreservesOriginalCause(bool project, string failure)
    {
        NativeTemplateSource source = Source(project);
        Exception original = failure switch
        {
            "invalidData" => new InvalidDataException("Changed source"),
            "io" => new IOException("Unreadable source"),
            "access" => new UnauthorizedAccessException("Denied source"),
            "json" => new JsonException("Malformed source"),
            _ => throw new ArgumentException("Unknown failure.", nameof(failure)),
        };
        source.ReadPackageFileAsync(PythonTemplateSourceReader.ManifestPath, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReadOnlyMemory<byte>>(original));

        Func<Task> qualify = () => project
            ? _policy.ValidateProjectAsync(source, CancellationToken.None)
            : _policy.DescribeItemAsync(source, CancellationToken.None);

        InvalidDataException exception = (await qualify.Should().ThrowAsync<InvalidDataException>().WithMessage("*owned sources*")).Which;
        exception.InnerException.Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qualify_PreCanceledToken_DoesNotReadOrRevalidateSource(bool project)
    {
        NativeTemplateSource source = Source(project);
        source.ClearReceivedCalls();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> qualify = () => project
            ? _policy.ValidateProjectAsync(source, cancellation.Token)
            : _policy.DescribeItemAsync(source, cancellation.Token);

        OperationCanceledException exception = (await qualify.Should().ThrowAsync<OperationCanceledException>()).Which;
        exception.CancellationToken.Should().Be(cancellation.Token);
        source.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PolicyAndAppend_NullContracts_IdentifyMissingArguments()
    {
        NativeTemplateSource source = Source(project: false);
        var output = WorkingDirectory.FromExplicit(Path.GetTempPath());
        Action handles = () => _policy.Handles(null!);
        Action missingSource = () => _policy.CreateAppendCapability(null!, output);
        Action missingOutput = () => _policy.CreateAppendCapability(source, null!);
        INativeTemplateAppendCapability capability = _policy.CreateAppendCapability(source, output);
        Func<Task> describe = () => _policy.DescribeItemAsync(null!, CancellationToken.None);
        Func<Task> validateProject = () => _policy.ValidateProjectAsync(null!, CancellationToken.None);
        Func<Task> validateAppend = () => capability.ValidateAsync(null!, CancellationToken.None);
        Func<Task> executeAppend = () => capability.ExecuteAsync(null!, CancellationToken.None);

        handles.Should().Throw<ArgumentNullException>().WithParameterName("selected");
        missingSource.Should().Throw<ArgumentNullException>().WithParameterName("selected");
        missingOutput.Should().Throw<ArgumentNullException>().WithParameterName("invocationOutputDirectory");
        await describe.Should().ThrowAsync<ArgumentNullException>().WithParameterName("selected");
        await validateProject.Should().ThrowAsync<ArgumentNullException>().WithParameterName("selected");
        await validateAppend.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
        await executeAppend.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Theory]
    [InlineData(null, "target", "digest")]
    [InlineData("", "target", "digest")]
    [InlineData("relative", "target", "digest")]
    [InlineData("root", null, "digest")]
    [InlineData("root", "", "digest")]
    [InlineData("root", "relative.py", "digest")]
    [InlineData("root", "target", null)]
    [InlineData("root", "target", "bad")]
    [InlineData("root", "target", "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task TargetGuard_InvalidContract_RefusesBeforeFileAccess(string? root, string? target, string? digest)
    {
        var guard = new PythonAppendTargetGuard();
        string? absoluteRoot = root == "root" ? Path.GetTempPath() : root;
        string? absoluteTarget = target == "target" ? Path.Combine(Path.GetTempPath(), "guard-contract.py") : target;
        digest = digest == "digest" ? new('A', 64) : digest;

        Func<Task> validate = () => guard.ValidateAsync(absoluteRoot!, absoluteTarget!, digest!, CancellationToken.None);

        await validate.Should().ThrowAsync<ArgumentException>();
    }

    private static NativeTemplateSource Source(bool project)
    {
        NativeTemplateSource source = Substitute.For<NativeTemplateSource>();
        source.PackageId.Returns(PythonTemplateSourceReader.PackageId);
        source.PackageVersion.Returns("1.0.0");
        source.Language.Returns("Python");
        source.IsProject.Returns(project);
        source.Identity.Returns(project ? "Azure.Functions.Python.Basic" : "Azure.Functions.Python.HttpTrigger");
        source.RevalidateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return source;
    }
}
