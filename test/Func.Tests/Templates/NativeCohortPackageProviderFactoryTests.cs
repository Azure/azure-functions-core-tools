// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeCohortPackageProviderFactoryTests
{
    [Fact]
    public void Constructor_NullComposition_IsRejected()
    {
        var factory = Substitute.For<ITemplatePackageProviderFactory>();
        Action missingFactory = () => new NativeCohortPackageProviderFactory(null!, new([], []));
        Action missingScope = () => new NativeCohortPackageProviderFactory(factory, null!);
        missingFactory.Should().Throw<ArgumentNullException>();
        missingScope.Should().Throw<ArgumentNullException>();
        NativeCohortPackageProviderFactory scoped = new(factory, new([], []));
        Action missingSettings = () => scoped.CreateProvider(null!);
        missingSettings.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ManagedRuntimeView_RejectsEveryLifecycleOrLatestQueryWithoutInvokingUnderlyingProvider()
    {
        var factory = Substitute.For<ITemplatePackageProviderFactory>();
        var provider = Substitute.For<IManagedTemplatePackageProvider>();
        var settings = Substitute.For<IEngineEnvironmentSettings>();
        factory.CreateProvider(settings).Returns(provider);
        NativeCohortPackageProviderFactory scoped = new(factory, new([], []));
        var view = (IManagedTemplatePackageProvider)scoped.CreateProvider(settings);
        Func<Task>[] operations =
        [
            () => view.InstallAsync([], CancellationToken.None),
            () => view.UninstallAsync([], CancellationToken.None),
            () => view.UpdateAsync([], CancellationToken.None),
            () => view.GetLatestVersionsAsync([], CancellationToken.None),
        ];

        foreach (var operation in operations)
        {
            await operation.Should().ThrowAsync<InvalidOperationException>().WithMessage("*read-only*");
        }

        provider.ReceivedCalls().Should().BeEmpty();
    }
}
