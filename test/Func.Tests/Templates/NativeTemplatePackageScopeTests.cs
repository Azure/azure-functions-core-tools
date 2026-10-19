// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Install;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplatePackageScopeTests
{
    [Fact]
    public void ReceiptBoundPin_RequiresOneActiveOwnerAndARealReceiptReader()
    {
        NativeTemplatePackagePin first = new("Owned", "1.0.0", new string('a', 64))
        {
            Origin = new BundledTemplateOrigin("Stack.A", "1.0.0", new string('c', 64), "source-a"),
        };
        NativeTemplatePackagePin other = first with
        {
            Origin = new BundledTemplateOrigin("Stack.B", "1.0.0", new string('d', 64), "source-b"),
        };
        NativeTemplateOwnershipReader reader = (_, _, _) => Task.FromResult(new NativeTemplateOwnership(true));

        new NativeTemplatePackageScope([first], [first]).ReceiptBoundPin(new("Owned", "1.0.0")).Should().BeNull();
        new NativeTemplatePackageScope([first], [], reader).ReceiptBoundPin(new("Owned", "1.0.0")).Should().BeNull();
        new NativeTemplatePackageScope([first], [first with { Origin = null }], reader)
            .ReceiptBoundPin(new("Owned", "1.0.0")).Should().BeNull();
        new NativeTemplatePackageScope([first, other], [first, other], reader)
            .ReceiptBoundPin(new("Owned", "1.0.0")).Should().BeNull();
        new NativeTemplatePackageScope([first], [first], reader).ReceiptBoundPin(new("Owned", "1.0.0")).Should().Be(first);
        new NativeTemplatePackageScope([first], [first], reader).ReceiptBoundPin(new("Unowned", "1.0.0")).Should().BeNull();
    }

    [Fact]
    public void SelectedPhysicalOwner_RestrictsAssetsWithoutMutatingTheOriginalScope()
    {
        NativeTemplatePackagePin first = new("Owned", "1.0.0", new string('a', 64))
        {
            Origin = new BundledTemplateOrigin("Stack.A", "1.0.0", new string('c', 64), "source-a"),
        };
        NativeTemplatePackagePin second = new("Owned", "2.0.0", new string('b', 64))
        {
            Origin = new BundledTemplateOrigin("Unrelated.B", "2.0.0", new string('d', 64), "source-b"),
        };
        NativeTemplatePackageScope original = new([first, second], [first, second]);

        NativeTemplatePackageScope selected = original.ForPhysicalOwner("stack.a", "1.0.0");

        selected.ExpectedHash(new("Owned", "1.0.0")).Sha256.Should().Be(first.Sha256);
        selected.ExpectedHash(new("Owned", "2.0.0")).Diagnostic.Should().Contain("not an active owned asset");
        original.ExpectedHash(new("Owned", "2.0.0")).Sha256.Should().Be(second.Sha256);
        Action missingOwner = () => original.ForPhysicalOwner(null!, "1.0.0");
        Action missingVersion = () => original.ForPhysicalOwner("Stack.A", null!);
        missingOwner.Should().Throw<ArgumentException>();
        missingVersion.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Scope_V1V2V1_SelectsCapturedAssetsRatherThanHighestVersionAndPreservesUnownedPackages()
    {
        NativeTemplatePackagePin first = new("Templates.Owned", "1.0.0", new string('a', 64));
        NativeTemplatePackagePin second = new("Templates.Owned", "2.0.0", new string('b', 64));
        NativeTemplatePackageScope old = new([first, second], [first]);
        NativeTemplatePackageScope current = new([first, second], [second]);

        old.Includes(new("Templates.Owned", "1.0.0")).Should().BeTrue();
        old.Includes(new("Templates.Owned", "2.0.0")).Should().BeFalse();
        current.Includes(new("Templates.Owned", "2.0.0")).Should().BeTrue();
        current.Includes(new("Templates.Owned", "1.0.0")).Should().BeFalse();
        old.Includes(new("templates.owned", "1.0")).Should().BeTrue();
        old.Includes(new("User.Template.Package", "99.0.0")).Should().BeTrue();
        var expected = old.ExpectedHash(new("Templates.Owned", "1.0.0"));
        expected.Sha256.Should().Be(first.Sha256);
        expected.Diagnostic.Should().BeNull();
    }

    [Fact]
    public void Scope_CallerMutatesCollections_RetainsItsOriginalCohort()
    {
        NativeTemplatePackagePin pin = new("Owned", "1.0.0", new string('a', 64));
        List<NativeTemplatePackagePin> owned = [pin, new("Owned", "2.0.0", new string('b', 64))];
        List<NativeTemplatePackagePin> active = [pin];
        NativeTemplatePackageScope scope = new(owned, active);

        owned.Clear();
        active.Clear();

        scope.Includes(new("Owned", "2.0.0")).Should().BeFalse();
        scope.ExpectedHash(new("Owned", "1.0.0")).Sha256.Should().Be(pin.Sha256);
    }

    [Fact]
    public void Scope_ConflictingHashesForSameSemanticVersion_ReportsAmbiguityWithoutChoosingAnOrigin()
    {
        NativeTemplatePackagePin first = new("Owned", "1.0", new string('a', 64));
        NativeTemplatePackagePin second = new("owned", "1.0.0", new string('b', 64));
        NativeTemplatePackageScope scope = new([first, second], [first, second]);

        var result = scope.ExpectedHash(new("Owned", "1.0.0"));

        result.Sha256.Should().BeNull();
        result.Diagnostic.Should().Contain("conflicting hashes");
    }

    [Fact]
    public async Task Scope_NewOwnedVersionAfterBoot_IsExcludedOnlyWhenOwnershipAndDigestAreProven()
    {
        NativeTemplatePackagePin first = new("Owned", "1.0.0", new string('a', 64));
        NativeTemplatePackageScope scope = new([first], [first], (_, package, _) =>
            Task.FromResult(package.Version == "2.0.0" ? new NativeTemplateOwnership(true, Sha256: new string('b', 64))
                : new NativeTemplateOwnership(false, "unknown user ownership")));

        var owned = await scope.ClassifyAsync(new("Owned", "2.0.0"), CancellationToken.None);
        var unknown = await scope.ClassifyAsync(new("Owned", "99.0.0"), CancellationToken.None);
        var active = await scope.ClassifyAsync(new("Owned", "1.0.0"), CancellationToken.None);

        owned.Include.Should().BeFalse();
        owned.OwnedSha256.Should().Be(new string('b', 64));
        unknown.Include.Should().BeTrue();
        unknown.Diagnostic.Should().Be("unknown user ownership");
        active.Include.Should().BeTrue();
    }

    [Fact]
    public async Task Scope_MalformedActiveReceipt_IsNotHiddenAsInactive()
    {
        NativeTemplatePackagePin first = new("Owned", "1.0.0", new string('a', 64));
        NativeTemplatePackageScope scope = new([first], [first], (_, _, _) =>
            Task.FromResult(new NativeTemplateOwnership(false, "malformed active receipt")));

        var classified = await scope.ClassifyAsync(new("Owned", "1.0.0"), CancellationToken.None);
        var verified = await scope.VerifyAsync(new("Owned", "1.0.0"), CancellationToken.None);

        classified.Include.Should().BeTrue();
        verified.Sha256.Should().BeNull();
        verified.Diagnostic.Should().Be("malformed active receipt");
    }

    [Fact]
    public void Scope_NullInputs_AreRejected()
    {
        Action owned = () => new NativeTemplatePackageScope(null!, []);
        Action active = () => new NativeTemplatePackageScope([], null!);
        owned.Should().Throw<ArgumentNullException>();
        active.Should().Throw<ArgumentNullException>();
        NativeTemplatePackageScope scope = new([], []);
        Action package = () => scope.ExpectedHash(null!);
        package.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("", "1.0.0", true)]
    [InlineData("Owned", "not-semver", true)]
    [InlineData("Owned", "1.0.0", false)]
    public void Scope_InvalidPins_AreRejectedWithoutGrantingPackageSelection(string id, string version, bool validHash)
    {
        NativeTemplatePackagePin pin = new(id, version, validHash ? new string('a', 64) : "invalid-hash");
        Action create = () => new NativeTemplatePackageScope([pin], [pin]);
        create.Should().Throw<ArgumentException>();
    }
}
