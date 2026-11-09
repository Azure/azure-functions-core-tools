// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class FlexByocTemplateContractTests
{
    private static readonly Assembly _workloadAssembly = typeof(DotNetWorkload).Assembly;

    [Fact]
    public void WorkloadAssembly_ContainsCompleteFlexByocTemplatePackage()
    {
        string[] resourceNames = _workloadAssembly.GetManifestResourceNames();

        resourceNames.Should().Contain(name => name.EndsWith("FlexByoc.Dockerfile.template", StringComparison.Ordinal));
        resourceNames.Should().Contain(name => name.EndsWith("FlexByoc.dockerignore.template", StringComparison.Ordinal));
        resourceNames.Should().Contain(name => name.EndsWith("FlexByoc.entrypoint.sh", StringComparison.Ordinal));
        resourceNames.Should().Contain(name => name.Contains("FlexByoc", StringComparison.Ordinal)
            && name.EndsWith("template.config.template.json", StringComparison.Ordinal));
        resourceNames.Should().Contain(name => name.Contains("FlexByoc", StringComparison.Ordinal)
            && name.EndsWith("template.config.func.host.json", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateManifest_DefinesDotNetRuntimeAndBindingContract()
    {
        using var manifest = JsonDocument.Parse(ReadResource("template.config.template.json"));
        JsonElement root = manifest.RootElement;

        root.GetProperty("identity").GetString().Should().Be("Azure.Functions.Cli.DotNet.FlexByoc");
        root.GetProperty("shortName").GetString().Should().Be("flex-byoc");
        JsonElement tags = root.GetProperty("tags");
        tags.GetProperty("type").GetString().Should().Be("artifact");
        tags.GetProperty("func-capability").GetString().Should().Be("flex-byoc");
        tags.GetProperty("func-stack").GetString().Should().Be("dotnet");

        JsonElement symbols = root.GetProperty("symbols");
        JsonElement runtimeVersion = symbols.GetProperty("RuntimeVersion");
        runtimeVersion.GetProperty("defaultValue").GetString().Should().Be("11");
        runtimeVersion.GetProperty("replaces").GetString().Should().Be("{{RUNTIME_VERSION}}");
        runtimeVersion.GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString())
            .Should().Equal("8", "9", "10", "11");

        AssertBinding(symbols.GetProperty("ProjectFile"), "host:func:dotnet-project-file", "{{PROJ_FILE}}");
        AssertBinding(symbols.GetProperty("AssemblyName"), "host:func:dotnet-assembly-name", "{{ASSEMBLY_NAME}}");
    }

    [Fact]
    public void TemplateContent_UsesDeclaredReplacementTokens()
    {
        string dockerfile = ReadResource("FlexByoc.Dockerfile.template");
        string entrypoint = ReadResource("FlexByoc.entrypoint.sh");

        dockerfile.Should().Contain("{{RUNTIME_VERSION}}");
        dockerfile.Should().Contain("{{PROJ_FILE}}");
        entrypoint.Should().Contain("{{ASSEMBLY_NAME}}");
        entrypoint.Should().NotContain("\r");
    }

    [Fact]
    public void HostManifest_ExposesRuntimeVersionOption()
    {
        using var manifest = JsonDocument.Parse(ReadResource("template.config.func.host.json"));

        manifest.RootElement.GetProperty("isHidden").GetBoolean().Should().BeTrue();
        manifest.RootElement.GetProperty("symbolInfo").GetProperty("RuntimeVersion")
            .GetProperty("longName").GetString().Should().Be("runtime-version");
    }

    private static void AssertBinding(JsonElement symbol, string binding, string replacement)
    {
        symbol.GetProperty("type").GetString().Should().Be("bind");
        symbol.GetProperty("binding").GetString().Should().Be(binding);
        symbol.GetProperty("replaces").GetString().Should().Be(replacement);
    }

    private static string ReadResource(string suffix)
    {
        string resourceName = _workloadAssembly.GetManifestResourceNames()
            .Single(name => name.Contains("FlexByoc", StringComparison.Ordinal)
                && name.EndsWith(suffix, StringComparison.Ordinal));
        using Stream stream = _workloadAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
