// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class NativeItemOutputIntentTests
{
    [Theory]
    [InlineData(false, "literal")]
    [InlineData(true, "legacy")]
    public void HostIntent_UsesImmutableContextValue(bool legacy, string expected)
    {
        NativeItemOutputIntent intent = legacy ? NativeItemOutputIntent.LegacyWorkflowRelative : NativeItemOutputIntent.LiteralOutput;
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(Path.GetTempPath()), itemOutputIntent: intent);
        using FuncTemplateEngineHost host = new(context);

        host.TryGetHostParamDefault("func:item-output-intent", out string? value).Should().BeTrue();

        value.Should().Be(expected);
        context.ItemOutputIntent.Should().Be(intent);
    }

    [Fact]
    public void Context_AbsentIntentIsLiteralAndInvalidIntentThrows()
    {
        var directory = WorkingDirectory.FromExplicit(Path.GetTempPath());
        new TemplateEngineContext(directory).ItemOutputIntent.Should().Be(NativeItemOutputIntent.LiteralOutput);

        Action invalid = () => _ = new TemplateEngineContext(directory, itemOutputIntent: (NativeItemOutputIntent)123);

        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SourceSnapshot_CopiesPinsAndRejectsInvalidAssociations()
    {
        TemplateEffectSource pin = new("Selected", "body.txt", new('A', 64));
        List<TemplateEffectSource> pins = [pin];
        List<NativeTemplateOutputSource> files = [new("body.txt", pin)];
        NativeTemplateSourceSnapshot snapshot = new("Selected", new('B', 64), pins, files);
        pins.Clear();
        files.Clear();

        snapshot.Sources.Should().ContainSingle().Which.Should().Be(pin);
        snapshot.Outputs.Should().ContainSingle();
        Action invalid = () => _ = new NativeTemplateSourceSnapshot("Selected", new('B', 64), [pin], [new("../body.txt", pin)]);
        invalid.Should().Throw<ArgumentException>();
        Action foreign = () => _ = new NativeTemplateSourceSnapshot("Selected", new('B', 64), [pin],
            [new("body.txt", new("Other", "body.txt", new('A', 64)))]);
        foreign.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("../body.txt")]
    public void SourceSnapshot_InvalidHashOrPathRefuses(string invalid)
    {
        TemplateEffectSource pin = new("Selected", invalid == "../body.txt" ? invalid : "body.txt", new('A', 64));

        Action create = () => _ = new NativeTemplateSourceSnapshot("Selected", invalid == "../body.txt" ? new('B', 64) : invalid,
            [pin], [new("body.txt", pin)]);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SourceSnapshot_NullCollectionsThrow()
    {
        Action sources = () => _ = new NativeTemplateSourceSnapshot("Selected", new('B', 64), null!, []);
        Action outputs = () => _ = new NativeTemplateSourceSnapshot("Selected", new('B', 64), [], null!);

        sources.Should().Throw<ArgumentNullException>().WithParameterName("sources");
        outputs.Should().Throw<ArgumentNullException>().WithParameterName("outputs");
    }
}
