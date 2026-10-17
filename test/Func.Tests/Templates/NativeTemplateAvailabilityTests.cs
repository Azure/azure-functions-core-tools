// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateAvailabilityTests
{
    [Fact]
    public async Task CaptureAsync_ConfiguredReceipt_SeparatesExactPhysicalAndLogicalMetadata()
    {
        var runtime = TestWorkloads.CreateInfo("Physical.win-x64", "9.2.0") with { Aliases = ["physical"] };
        WorkloadEntry entry = new()
        {
            PackageId = runtime.PackageId,
            PackageVersion = runtime.PackageVersion,
            Aliases = runtime.Aliases,
            LogicalPackage = new() { PackageId = "Published", PackageVersion = "1.5.0", Aliases = ["public-alias"] },
        };
        var capture = Completed(entry, runtime);
        var provider = new NativeTemplateAvailability(new WorkloadProvider([runtime]), capture);

        var snapshot = await provider.CaptureAsync(CancellationToken.None);

        snapshot.Workloads.Should().SatisfyRespectively(
            physical =>
            {
                physical.PackageId.Should().Be("Physical.win-x64");
                physical.Version.ToNormalizedString().Should().Be("9.2.0");
                physical.Aliases.Should().Equal("physical");
                physical.PublishedPackageId.Should().BeNull();
                physical.IsUsable.Should().BeTrue();
            },
            logical =>
            {
                logical.PackageId.Should().Be("Published");
                logical.Version.ToNormalizedString().Should().Be("1.5.0");
                logical.Aliases.Should().Equal("public-alias");
                logical.IsUsable.Should().BeTrue();
            });
        FuncTemplateConstraintEvaluator evaluator = new(new());
        var declarations = TemplateConstraintDeclarationReader.Read("""
            {"constraints":{
              "physical":{"type":"func-workload","args":{"id":"physical","version":"[9.0,10.0)"}},
              "logical":{"type":"func-workload","args":{"id":"public-alias","version":"[1.0,2.0)"}}
            }}
            """);
        evaluator.EvaluateAll(declarations, snapshot).Should().Be(FuncTemplateConstraintOutcome.Eligible);
        var wrongLogicalVersion = TemplateConstraintDeclarationReader.Read(
            """{"constraints":{"logical":{"type":"func-workload","args":{"id":"Published","version":"[9.0,10.0)"}}}}""")[0];
        evaluator.Evaluate(wrongLogicalVersion, snapshot).Should().Be(FuncTemplateConstraintOutcome.Restricted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureAsync_FailedReceipt_NeverBecomesUsableFromProviderPresence(bool loaded)
    {
        var runtime = loaded ? TestWorkloads.CreateInfo() : null;
        var capture = Completed(Entry(), runtime, "load or configuration failed");
        WorkloadInfo[] rows = runtime is null ? [] : [runtime];
        var provider = new NativeTemplateAvailability(new WorkloadProvider(rows), capture);

        var snapshot = await provider.CaptureAsync(CancellationToken.None);

        snapshot.Workloads.Should().ContainSingle().Which.IsUsable.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureAsync_NoCompletedBoot_RefusesInsteadOfInferringFromInventory()
    {
        var runtime = TestWorkloads.CreateInfo();
        var provider = new NativeTemplateAvailability(new WorkloadProvider([runtime]), new());

        Func<Task> capture = () => provider.CaptureAsync(CancellationToken.None);

        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*explicitly completed*");
    }

    [Fact]
    public async Task CaptureAsync_DifferentLoadedInstance_RefusesCrossHostReceipts()
    {
        var original = TestWorkloads.CreateInfo();
        var other = TestWorkloads.CreateInfo();
        var provider = new NativeTemplateAvailability(new WorkloadProvider([other]), Completed(Entry(), original));

        Func<Task> capture = () => provider.CaptureAsync(CancellationToken.None);

        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not belong*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    public async Task CaptureAsync_MalformedVersion_PropagatesExplicitMetadataFailure(string version)
    {
        var runtime = TestWorkloads.CreateInfo(version: version);
        var entry = new WorkloadEntry { PackageId = runtime.PackageId, PackageVersion = version };
        var provider = new NativeTemplateAvailability(new WorkloadProvider([runtime]), Completed(entry, runtime));

        Func<Task> capture = () => provider.CaptureAsync(CancellationToken.None);

        await capture.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task CaptureAsync_ContentInventory_DoesNotInventUsabilityOrBundleAuthority()
    {
        var capture = new NativeTemplateWorkloadCapture();
        capture.Begin();
        capture.Complete();
        var provider = new NativeTemplateAvailability(new WorkloadProvider([TestWorkloads.CreateContentInfo("Bundles")]), capture);

        var snapshot = await provider.CaptureAsync(CancellationToken.None);

        snapshot.Workloads.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAsync_AliasesChangedAfterBoot_CannotMutateReceiptOrEarlierSnapshot()
    {
        List<string> aliases = ["original"];
        var runtime = TestWorkloads.CreateInfo();
        var entry = new WorkloadEntry { PackageId = runtime.PackageId, PackageVersion = runtime.PackageVersion, Aliases = aliases };
        var provider = new NativeTemplateAvailability(new WorkloadProvider([runtime]), Completed(entry, runtime));
        var before = await provider.CaptureAsync(CancellationToken.None);

        aliases.Clear();
        aliases.Add("changed");
        var after = await provider.CaptureAsync(CancellationToken.None);

        before.Workloads.Single().Aliases.Should().Equal("original");
        after.Workloads.Single().Aliases.Should().Equal("original");
    }

    [Fact]
    public async Task CaptureAsync_UnexpectedProviderFault_Propagates()
    {
        var runtime = TestWorkloads.CreateInfo();
        var inventory = Substitute.For<IWorkloadProvider>();
        inventory.GetRuntimeWorkloads().Throws(new InvalidOperationException("provider fault"));
        var provider = new NativeTemplateAvailability(inventory, Completed(Entry(), runtime));

        Func<Task> capture = () => provider.CaptureAsync(CancellationToken.None);

        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("provider fault");
    }

    [Fact]
    public async Task CaptureAsync_Canceled_DoesNotInspectProvider()
    {
        var inventory = Substitute.For<IWorkloadProvider>();
        var provider = new NativeTemplateAvailability(inventory, new());
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> capture = () => provider.CaptureAsync(cancellation.Token);

        await capture.Should().ThrowAsync<OperationCanceledException>();
        inventory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        Action noInventory = () => new NativeTemplateAvailability(null!, new());
        Action noCapture = () => new NativeTemplateAvailability(new WorkloadProvider([]), null!);

        noInventory.Should().Throw<ArgumentNullException>().WithParameterName("workloads");
        noCapture.Should().Throw<ArgumentNullException>().WithParameterName("capture");
    }

    [Fact]
    public void Capture_LifecycleAndSourceKind_RefusesInvalidUse()
    {
        var capture = new NativeTemplateWorkloadCapture();
        Action beforeBoot = () => capture.Record(Entry(), TestWorkloads.CreateInfo());
        beforeBoot.Should().Throw<InvalidOperationException>();
        capture.Begin();
        Action repeatedBoot = capture.Begin;
        Action invalidKind = () => capture.Record(new() { PackageId = "content", PackageVersion = "1.0", Kind = WorkloadKind.Content }, null);
        Action unprovedSuccess = () => capture.Record(Entry(), null);
        repeatedBoot.Should().Throw<InvalidOperationException>();
        invalidKind.Should().Throw<ArgumentException>();
        unprovedSuccess.Should().Throw<ArgumentException>();
        capture.Complete();
        Action afterBoot = () => capture.Record(Entry(), TestWorkloads.CreateInfo());
        afterBoot.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Capture_MismatchedLoadedIdentityOrMalformedLogicalMetadata_RefusesReceipt()
    {
        NativeTemplateWorkloadCapture capture = new();
        capture.Begin();
        Action mismatched = () => capture.Record(Entry(), TestWorkloads.CreateInfo("Another"));
        WorkloadEntry malformed = new()
        {
            PackageId = "Test.Workload.A", PackageVersion = "1.0.0",
            LogicalPackage = new() { PackageId = null!, PackageVersion = "1.0.0" },
        };
        Action invalidLogical = () => capture.Record(malformed, TestWorkloads.CreateInfo());

        mismatched.Should().Throw<ArgumentException>().WithParameterName("workload");
        invalidLogical.Should().Throw<InvalidDataException>();
    }

    private static WorkloadEntry Entry() => new() { PackageId = "Test.Workload.A", PackageVersion = "1.0.0" };

    private static NativeTemplateWorkloadCapture Completed(WorkloadEntry entry, RuntimeWorkloadInfo? runtime, string? failure = null)
    {
        NativeTemplateWorkloadCapture capture = new();
        capture.Begin();
        capture.Record(entry, runtime, failure);
        capture.Complete();
        return capture;
    }
}
