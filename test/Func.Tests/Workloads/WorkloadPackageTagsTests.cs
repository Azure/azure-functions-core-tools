// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Tests.Workloads;

public sealed class WorkloadPackageTagsTests
{
    [Fact]
    public void ParseValues_MixedTags_ReturnsNormalizedMatchingValues()
    {
        IReadOnlyList<string> values = WorkloadPackageTags.ParseValues(
            "ALIAS:Python, language:python alias:NodeJs alias:",
            WorkloadPackageTags.AliasPrefix);

        values.Should().Equal(["python", "nodejs"]);
    }

    [Fact]
    public void ParseLastValue_DuplicateTags_ReturnsLastNonEmptyValue()
    {
        string? value = WorkloadPackageTags.ParseLastValue(
            "kind:workload kind: kind:RID-POINTER",
            WorkloadPackageTags.KindPrefix);

        value.Should().Be("rid-pointer");
    }

    [Fact]
    public void ParseValues_MissingTags_ReturnsEmpty()
        => WorkloadPackageTags.ParseValues(null, WorkloadPackageTags.AliasPrefix).Should().BeEmpty();
}
