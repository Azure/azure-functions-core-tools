// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateConstraintRequirementMatcherTests
{
    private readonly TemplateConstraintRequirementMatcher _matcher = new();

    [Theory]
    [InlineData("node", "[1.0,2.0)", "1.5.0", true)]
    [InlineData("NODE", "1.0", "1.0.0-preview.2", true)]
    [InlineData("node", "[1.0,2.0)", "2.0.0-preview.1", true)]
    [InlineData("node", "[1.0,2.0)", "2.0.0", false)]
    [InlineData("node", "[1.0]", "1.0.0-preview.2", true)]
    [InlineData("node", "[2.0.0-preview.1]", "2.0.0-preview.1", true)]
    [InlineData("node", null, "3.0.0", true)]
    [InlineData("python", null, "1.5.0", false)]
    [InlineData("Azure.Functions.Workloads.Node", null, "1.5.0", true)]
    [InlineData("Azure.Functions.Workloads.Node.win-x64", null, "1.5.0", true)]
    public void MatchesWorkload_IdentityAndVersion_ReturnsExpected(string identity, string? range, string version, bool expected)
    {
        var requirement = new TemplateConstraintAlternative(identity, range is null ? null : VersionRange.Parse(range));

        bool result = _matcher.MatchesWorkload(requirement, "Azure.Functions.Workloads.Node", NuGetVersion.Parse(version), ["node"], "Azure.Functions.Workloads.Node.win-x64");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "[4.*,5.0.0)", "bundle", "4.20.0", true)]
    [InlineData(null, "[4.*,5.0.0)", "bundle", "5.0.0-preview.1", true)]
    [InlineData(null, "[4.*,5.0.0)", "bundle", "5.0.0", false)]
    [InlineData("BUNDLE", "4.20", "bundle", "4.20.0-preview.2", true)]
    [InlineData("other", null, "bundle", "4.20.0", false)]
    [InlineData(null, null, null, null, false)]
    [InlineData(null, null, "bundle", null, false)]
    [InlineData(null, null, null, "4.20.0", false)]
    [InlineData(null, null, "bundle", "4.20.0", true)]
    public void MatchesBundle_ResolvedContext_ReturnsExpected(string? identity, string? range, string? bundleId, string? version, bool expected)
    {
        var requirement = new TemplateConstraintAlternative(identity, range is null ? null : VersionRange.Parse(range));

        bool result = _matcher.MatchesBundle(requirement, bundleId, version is null ? null : NuGetVersion.Parse(version));

        result.Should().Be(expected);
    }

    [Fact]
    public void MatchesWorkload_MissingRequirementIdentity_Throws()
    {
        var requirement = new TemplateConstraintAlternative(null, null);
        Action action = () => _matcher.MatchesWorkload(requirement, "node", NuGetVersion.Parse("1.0"), []);

        action.Should().Throw<ArgumentException>().WithParameterName("requirement");
    }

    [Fact]
    public void MatchesBundle_NullRequirement_Throws()
    {
        Action action = () => _matcher.MatchesBundle(null!, "bundle", NuGetVersion.Parse("4.20"));

        action.Should().Throw<ArgumentNullException>().WithParameterName("requirement");
    }

    [Fact]
    public void MatchesWorkload_NullRequirement_Throws()
    {
        Action action = () => _matcher.MatchesWorkload(null!, "node", NuGetVersion.Parse("1.0"), []);

        action.Should().Throw<ArgumentNullException>().WithParameterName("requirement");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MatchesWorkload_InvalidPackageId_Throws(string? packageId)
    {
        var requirement = new TemplateConstraintAlternative("node", null);
        Action action = () => _matcher.MatchesWorkload(requirement, packageId!, NuGetVersion.Parse("1.0"), []);

        action.Should().Throw<ArgumentException>().WithParameterName("packageId");
    }

    [Fact]
    public void MatchesWorkload_NullVersion_Throws()
    {
        var requirement = new TemplateConstraintAlternative("node", null);
        Action action = () => _matcher.MatchesWorkload(requirement, "node", null!, []);

        action.Should().Throw<ArgumentNullException>().WithParameterName("version");
    }

    [Fact]
    public void MatchesWorkload_NullAliases_Throws()
    {
        var requirement = new TemplateConstraintAlternative("node", null);
        Action action = () => _matcher.MatchesWorkload(requirement, "node", NuGetVersion.Parse("1.0"), null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("aliases");
    }
}