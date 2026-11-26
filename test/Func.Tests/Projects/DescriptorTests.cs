// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Tests.Projects;

public class DescriptorTests
{
    [Fact]
    public void StackEquality_UsesOnlyTheCaseInsensitiveCliId()
    {
        StackDescriptor stable = new("comet", "native", "Comet");
        StackDescriptor preview = new("CoMeT", "comet-worker", "Comet Preview", "New description");
        StackDescriptor other = new("other", "native", "Comet");

        (stable == preview).Should().BeTrue();
        (stable != other).Should().BeTrue();
        new HashSet<StackDescriptor> { stable, preview, other }.Should().HaveCount(2);
    }

    [Fact]
    public void LanguageMatchesOnlyItsIdentifierAndSnapshotOfExplicitAliases()
    {
        string[] input = ["c#"];
        LanguageDescriptor language = new("csharp", "Renamed language", input);
        LanguageDescriptor renamed = new("CSharp", "C#", "cs");
        input[0] = "changed";

        (language == renamed).Should().BeTrue();
        (language != new LanguageDescriptor("cs", "Renamed language", "c#")).Should().BeTrue();
        new HashSet<LanguageDescriptor> { language, renamed }.Should().ContainSingle();
        language.IsMatch(" CSharp ").Should().BeTrue();
        language.IsMatch(" C# ").Should().BeTrue();
        language.IsMatch("Renamed language").Should().BeFalse();
        language.IsMatch("changed").Should().BeFalse();
        language.IsMatch(null).Should().BeFalse();
        language.IsMatch("  ").Should().BeFalse();
        Action mutateSnapshot = () => ((IList<string>)language.Aliases)[0] = "changed";
        mutateSnapshot.Should().Throw<NotSupportedException>();
    }
}
