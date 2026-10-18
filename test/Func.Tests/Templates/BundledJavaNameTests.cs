// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData(" M\u00c9 App_42! ", "Default Name", "m-app_42")]
    [InlineData("\u00c9\u4e2d\u6587\u0661", "Default Name", "azure-functions-java")]
    [InlineData("App\u0661\u0662\u0663_42", "Default Name", "app_42")]
    [InlineData("\ud83d\ude80My App", "Default Name", "my-app")]
    [InlineData(" My App_42! ", "Default Name", "my-app_42")]
    [InlineData("!!!", "Default Name", "azure-functions-java")]
    [InlineData("-my-app", "Default Name", "-my-app")]
    [InlineData("CON", "Default Name", "con")]
    [InlineData("go", "Default Name", "go")]
    [InlineData("toolchain", "Default Name", "toolchain")]
    [InlineData("_", "Default Name", "_")]
    [InlineData("123", "Default Name", "123")]
    [InlineData(null, "M\u00c9 Project", "m-project")]
    [InlineData("", "M\u00c9 Project", "m-project")]
    [InlineData(null, "\u4e2d\u6587", "azure-functions-java")]
    public async Task JavaArtifactName_NativeAndPhysical_DeriveExpectedMavenName(string? name, string directoryName, string expected)
    {
        using var services = await InstallAsync("Java");
        var native = WorkingDirectory.FromExplicit(Path.Combine(_root, "native", directoryName));
        var physical = WorkingDirectory.FromExplicit(Path.Combine(_root, "physical", directoryName));
        Directory.CreateDirectory(physical.Info.FullName);
        using var templater = ProjectTemplater(services, native, "java");
        var selected = (await templater.ResolveGroupAsync("Azure.Functions.Java.Basic", TemplateType.Project, CancellationToken.None)).Group!.Single();
        var initializer = services.GetRequiredService<IProjectInitializer>();
        var command = new RootCommand();
        initializer.GetInitOptions(new InitOptionRegistry(command));

        var created = await selected.InvokeAsync(new(native, new Dictionary<string, string?>(), name), CancellationToken.None);
        await initializer.InitializeAsync(new(physical, name, Language: null, Force: false), command.Parse([]), CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.ReadAllText(Path.Combine(native.Info.FullName, "pom.xml")).Should().Contain("<artifactId>" + expected + "</artifactId>")
            .And.Contain("<functionAppName>" + expected + "</functionAppName>");
        File.ReadAllBytes(Path.Combine(physical.Info.FullName, "pom.xml")).Should()
            .Equal(File.ReadAllBytes(Path.Combine(native.Info.FullName, "pom.xml")));
    }
}
