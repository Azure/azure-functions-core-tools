// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class ResolvedTemplateTests
{
    [Fact]
    public async Task InvokeAsync_ExplicitInvoker_ReceivesCopiedCanonicalInputsAndToken()
    {
        var invoker = Substitute.For<ISelectedTemplateInvoker>();
        TemplateInvocationRequest? captured = null;
        TemplateInvocationResult expected = new(TemplateInvocationStatus.Success, []);
        invoker.InvokeAsync(Arg.Do<TemplateInvocationRequest>(request => captured = request), Arg.Any<CancellationToken>()).Returns(expected);
        var selected = new ResolvedTemplate(Entry(), invoker);
        Dictionary<string, string?> values = new(StringComparer.Ordinal) { ["Value"] = "original" };
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Path.GetTempPath()), values, "Name");
        using CancellationTokenSource cancellation = new();

        var result = await selected.InvokeAsync(request, cancellation.Token);
        values["Value"] = "changed";

        result.Should().BeSameAs(expected);
        captured.Should().NotBeSameAs(request);
        captured!.Parameters.Should().Contain("Value", "original");
        await invoker.Received(1).InvokeAsync(captured, cancellation.Token);
    }

    [Fact]
    public async Task InvokeAsync_UnknownParameter_DoesNotCallInvoker()
    {
        var invoker = Substitute.For<ISelectedTemplateInvoker>();
        var selected = new ResolvedTemplate(Entry(), invoker);
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Path.GetTempPath()),
            new Dictionary<string, string?> { ["unknown"] = "value" });

        var result = await selected.InvokeAsync(request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Fact]
    public async Task InvokeAsync_PreCanceled_DoesNotCallInvoker()
    {
        var invoker = Substitute.For<ISelectedTemplateInvoker>();
        var selected = new ResolvedTemplate(Entry(), invoker);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Path.GetTempPath()), new Dictionary<string, string?>());

        Func<Task> invoke = () => selected.InvokeAsync(request, cancellation.Token);

        await invoke.Should().ThrowAsync<OperationCanceledException>();
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_NullDependency_Throws(bool missingEntry)
    {
        Action create = () => _ = new ResolvedTemplate(missingEntry ? null! : Entry(),
            missingEntry ? Substitute.For<ISelectedTemplateInvoker>() : null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName(missingEntry ? "entry" : "invoker");
    }

    private static TemplateCatalogEntry Entry() => new("test", "test", null, TemplateType.Item, [], null, null, 0, null, false,
        [new("Value", "Value", null, null, "string", [], null, false, false, false)], null)
    {
        ConstraintEvaluation = TemplateConstraintEvaluation.Eligible,
    };
}
