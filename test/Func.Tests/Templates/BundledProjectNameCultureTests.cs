// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Globalization;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData("Go", "go", "tr-TR", "PIN", "Default Name", "pin")]
    [InlineData("Go", "go", "tr-TR", "I", "Default Name", "i")]
    [InlineData("Go", "go", "tr-TR", null, "PIN", "pin")]
    [InlineData("Go", "go", "tr-TR", null, "I", "i")]
    [InlineData("Go", "go", "tr-TR", "MY API", "Default Name", "my-api")]
    [InlineData("Go", "go", "tr-TR", " My App_42! ", "Default Name", "my-app_42")]
    [InlineData("Go", "go", "en-US", "PIN", "Default Name", "pin")]
    [InlineData("Go", "go", "en-US", "I", "Default Name", "i")]
    [InlineData("Java", "java", "tr-TR", "PIN", "Default Name", "pin")]
    [InlineData("Java", "java", "tr-TR", "I", "Default Name", "i")]
    [InlineData("Java", "java", "tr-TR", null, "PIN", "pin")]
    [InlineData("Java", "java", "tr-TR", null, "I", "i")]
    [InlineData("Java", "java", "tr-TR", "MY API", "Default Name", "my-api")]
    [InlineData("Java", "java", "tr-TR", " My App_42! ", "Default Name", "my-app_42")]
    [InlineData("Java", "java", "en-US", "PIN", "Default Name", "pin")]
    [InlineData("Java", "java", "en-US", "I", "Default Name", "i")]
    public async Task ProjectName_Culture_NativeAndPhysicalUseInvariantCasing(
        string stack, string alias, string culture, string? name, string directoryName, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var services = await InstallAsync(stack);
            var native = WorkingDirectory.FromExplicit(Path.Combine(_root, "native", directoryName));
            var physical = WorkingDirectory.FromExplicit(Path.Combine(_root, "physical", directoryName));
            Directory.CreateDirectory(physical.Info.FullName);
            using var templater = ProjectTemplater(services, native, alias);
            var selected = (await templater.ResolveGroupAsync(
                $"Azure.Functions.{stack}.Basic", TemplateType.Project, CancellationToken.None)).Group!.Single();
            var initializer = services.GetRequiredService<IProjectInitializer>();
            var command = new RootCommand();
            initializer.GetInitOptions(new InitOptionRegistry(command));
            Dictionary<string, string?> parameters = stack == "Go" ? new() { ["SkipGoModTidy"] = "true" } : [];

            var created = await selected.InvokeAsync(new(native, parameters, name), CancellationToken.None);
            await initializer.InitializeAsync(new(physical, name, Language: null, Force: false),
                command.Parse(stack == "Go" ? ["--skip-go-mod-tidy"] : []), CancellationToken.None);

            created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
            string manifest = stack == "Go" ? "go.mod" : "pom.xml";
            string content = File.ReadAllText(Path.Combine(native.Info.FullName, manifest));
            if (stack == "Go")
            {
                content.Split('\n')[0].TrimEnd('\r').Should().Be("module " + expected);
            }
            else
            {
                content.Should().Contain("<artifactId>" + expected + "</artifactId>")
                    .And.Contain("<functionAppName>" + expected + "</functionAppName>");
            }
            File.ReadAllBytes(Path.Combine(physical.Info.FullName, manifest)).Should()
                .Equal(File.ReadAllBytes(Path.Combine(native.Info.FullName, manifest)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
