// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public sealed class PythonTemplateApplicationPlannerTests
{
    private readonly PythonTemplateApplicationPlanner _planner = new();
    private readonly string _root = Path.GetFullPath("python-test-project");

    [Theory]
    [InlineData(null, false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    public async Task PlanAsync_ApprovedDefaultTargetState_ExpandsOnlyTheSelectedLegacyJob(string? digest, bool append)
    {
        PythonTemplateApplicationMode expectedMode = append ? PythonTemplateApplicationMode.AppendToFile : PythonTemplateApplicationMode.CreateNewApp;
        List<PythonTemplateApplicationMode> expanded = [];
        using CancellationTokenSource cancellation = new();

        PythonTemplateApplication plan = await _planner.PlanAsync(_root, "function_app.py", append, digest, (mode, token) =>
        {
            token.Should().Be(cancellation.Token);
            expanded.Add(mode);
            return Task.FromResult(mode == PythonTemplateApplicationMode.CreateNewApp ? "whole app" : "function body");
        }, cancellation.Token);

        expanded.Should().Equal(expectedMode);
        plan.Mode.Should().Be(expectedMode);
        plan.TargetPath.Should().Be(Path.Combine(_root, "function_app.py"));
        plan.ExpectedTargetSha256.Should().Be(digest);
        plan.Content.Should().Be(expectedMode == PythonTemplateApplicationMode.CreateNewApp ? "whole app" : "function body");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PlanAsync_EmptyNativeExpansion_RefusesInsteadOfReportingNoOpSuccess(string? content)
    {
        Func<Task> plan = () => _planner.PlanAsync(_root, "function_app.py", false, null, (_, _) => Task.FromResult(content!), CancellationToken.None);

        await plan.Should().ThrowAsync<InvalidOperationException>().WithMessage("*did not produce content*");
    }

    [Fact]
    public async Task PlanAsync_PreCanceled_DoesNotExpandSource()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool expanded = false;

        Func<Task> plan = () => _planner.PlanAsync(_root, "function_app.py", false, null, (_, _) =>
        {
            expanded = true;
            return Task.FromResult("content");
        }, cancellation.Token);

        await plan.Should().ThrowAsync<OperationCanceledException>();
        expanded.Should().BeFalse();
    }

    [Fact]
    public async Task PlanAsync_CancellationDuringExpansion_DoesNotReturnApplicablePlan()
    {
        using CancellationTokenSource cancellation = new();
        Func<Task> plan = () => _planner.PlanAsync(_root, "function_app.py", false, null, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult("content");
        }, cancellation.Token);

        await plan.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("relative", null)]
    [InlineData("project", "")]
    [InlineData("project", "invalid")]
    public async Task PlanAsync_InvalidPathOrDigest_RefusesBeforeExpansion(string? path, string? digest)
    {
        bool expanded = false;
        Func<Task> plan = () => _planner.PlanAsync(path == "project" ? _root : path!, "function_app.py", false, digest, (_, _) =>
        {
            expanded = true;
            return Task.FromResult("content");
        }, CancellationToken.None);

        await plan.Should().ThrowAsync<ArgumentException>();
        expanded.Should().BeFalse();
    }

    [Fact]
    public async Task PlanAsync_NullExpansionCallback_Throws()
    {
        Func<Task> plan = () => _planner.PlanAsync(_root, "function_app.py", false, null, null!, CancellationToken.None);

        await plan.Should().ThrowAsync<ArgumentNullException>().WithParameterName("expandNativeSource");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlanAsync_DoubleSuffixedLegacyCreateTarget_KeepsSelectionAndAppendDestinationsDistinct(bool createTargetExists)
    {
        const string digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        PythonTemplateApplication plan = await _planner.PlanAsync(_root, "function_app.py.py", createTargetExists, digest,
            (_, _) => Task.FromResult("native content"), CancellationToken.None);

        plan.Mode.Should().Be(createTargetExists ? PythonTemplateApplicationMode.AppendToFile : PythonTemplateApplicationMode.CreateNewApp);
        plan.TargetPath.Should().Be(Path.Combine(_root, createTargetExists ? "function_app.py" : "function_app.py.py"));
        plan.ExpectedTargetSha256.Should().Be(createTargetExists ? digest : null);
    }

    [Fact]
    public async Task PlanAsync_CreateTargetExistsButAppendTargetAbsent_RefusesBeforeSourceExpansion()
    {
        bool expanded = false;
        Func<Task> plan = () => _planner.PlanAsync(_root, "function_app.py.py", true, null, (_, _) =>
        {
            expanded = true;
            return Task.FromResult("native content");
        }, CancellationToken.None);

        await plan.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append target must exist*");
        expanded.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../function_app.py")]
    [InlineData("custom.py")]
    public async Task PlanAsync_UnknownCreateTarget_RefusesBeforeExpansion(string? createTarget)
    {
        Func<Task> plan = () => _planner.PlanAsync(_root, createTarget!, false, null,
            (_, _) => Task.FromResult("content"), CancellationToken.None);

        await plan.Should().ThrowAsync<ArgumentException>();
    }
}
