// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Tests.Workloads;

// WorkloadKind is internal, so it cannot appear in the signature of a public
// test method (CS0051). Kinds are named inside the bodies rather than passed
// as [InlineData] arguments.
public class WorkloadKindExtensionsTests
{
    [Fact]
    public void ToWireValue_ReturnsManifestValue()
    {
        WorkloadKind.Workload.ToWireValue().Should().Be("workload");
        WorkloadKind.Content.ToWireValue().Should().Be("content");
        WorkloadKind.Meta.ToWireValue().Should().Be("meta");
        WorkloadKind.RidPointer.ToWireValue().Should().Be("rid-pointer");
    }

    [Fact]
    public void ToWireValue_Throws_WhenKindIsUndefined()
    {
        var kind = (WorkloadKind)99;

        FluentActions.Invoking(() => kind.ToWireValue()).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryParseWireValue_ReturnsKind_ForManifestValue()
    {
        ShouldParseAs("workload", WorkloadKind.Workload);
        ShouldParseAs("content", WorkloadKind.Content);
        ShouldParseAs("meta", WorkloadKind.Meta);
        ShouldParseAs("rid-pointer", WorkloadKind.RidPointer);
    }

    [Fact]
    public void TryParseWireValue_RoundTrips_EveryKind()
    {
        foreach (WorkloadKind expected in Enum.GetValues<WorkloadKind>())
        {
            ShouldParseAs(expected.ToWireValue(), expected);
        }
    }

    [Theory]
    [InlineData("Workload")]
    [InlineData("ridPointer")]
    [InlineData("rid_pointer")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseWireValue_ReturnsFalse_ForUnrecognizedValue(string? value)
    {
        bool parsed = WorkloadKind.TryParseWireValue(value, out WorkloadKind kind);

        parsed.Should().BeFalse();
        kind.Should().Be(default(WorkloadKind));
    }

    private static void ShouldParseAs(string value, WorkloadKind expected)
    {
        bool parsed = WorkloadKind.TryParseWireValue(value, out WorkloadKind kind);

        parsed.Should().BeTrue($"'{value}' is a known workload kind");
        kind.Should().Be(expected);
    }
}
