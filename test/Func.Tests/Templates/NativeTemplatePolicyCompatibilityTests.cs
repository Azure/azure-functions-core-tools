// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Security.Cryptography;
using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplatePolicyCompatibilityTests
{
    [Fact]
    public void UnchangedOldBuilderAndWorkload_LoadAgainstExtendedHostWithoutAbstractMemberRequirement()
    {
        string root = Directory.CreateTempSubdirectory("old-native-policy-boundary-").FullName;
        WorkloadLoadContext? context = null;
        try
        {
            string binary = Path.Combine(root, "LegacyNativePolicyBoundary.dll");
            using (Stream embedded = typeof(NativeTemplatePolicyCompatibilityTests).Assembly.GetManifestResourceStream(
                "Azure.Functions.Cli.Tests.LegacyNativePolicyBoundary.bin")!)
            using (FileStream output = File.Create(binary))
            {
                embedded.CopyTo(output);
            }
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(binary)))
                .Should().Be("c10671fd8592d2807c44cf2da01e87527e1ae24b54fd47cbc1082907e8c3ccb4");
            var paths = Substitute.For<IWorkloadPaths>();
            paths.GetInstallDirectory("Legacy.Policy", "1.0.0").Returns(root);
            RuntimeWorkloadInfo loaded = new WorkloadLoader(paths).Load([new WorkloadEntry
            {
                PackageId = "Legacy.Policy",
                PackageVersion = "1.0.0",
                EntryPoint = new() { AssemblyPath = "LegacyNativePolicyBoundary.dll", Type = "LegacyNativePolicyBoundary.LegacyWorkload" },
            }]).Single();
            context = loaded.LoadContext;
            Assembly assembly = loaded.Instance.GetType().Assembly;
            ServiceCollection services = new();
            Type builderType = assembly.GetType("LegacyNativePolicyBoundary.LegacyBuilder", throwOnError: true)!;
            var builder = (FunctionsCliBuilder)Activator.CreateInstance(builderType, services)!;

            loaded.Instance.Configure(new DefaultFunctionsCliBuilder(services, loaded));

            services.Should().ContainSingle().Which.ServiceType.FullName.Should().Be("LegacyNativePolicyBoundary.LegacyMarker");
            builderType.GetMethod(nameof(FunctionsCliBuilder.AddNativeTemplatePolicy))!.DeclaringType.Should().Be(typeof(FunctionsCliBuilder));
            Action unsupported = builder.AddNativeTemplatePolicy<OriginalEightPolicy>;
            unsupported.Should().Throw<NotSupportedException>().WithMessage("*Update the Azure Functions CLI*");
            services.Should().NotContain(service => service.ServiceType == typeof(WorkloadNativeTemplatePolicyRegistration));
            assembly.GetReferencedAssemblies().Should().NotContain(reference => reference.Name == "func");
        }
        finally
        {
            context?.Unload();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // The collectible old assembly may remain mapped until GC releases its Windows handle.
            }
        }
    }

    [Fact]
    public void SharedBoundary_ContainsOnlyTheVettedTypesAndNoEngineOrContainerGraph()
    {
        Type[] expected =
        [
            typeof(NativeTemplateSource), typeof(NativeTemplateSourceStamp), typeof(NativeTemplateAppendMapping),
            typeof(NativeItemPolicyDescription), typeof(NativeTemplateAppendRequest), typeof(NativeTemplateAppendResult),
            typeof(INativeTemplateAppendCapability), typeof(NativeTemplatePolicy), typeof(NativeTemplatePolicyStages),
            typeof(NativeTemplateInvocationContext), typeof(NativeTemplateCapturedFile), typeof(NativeTemplateCapturedOutput),
            typeof(NativeTemplateProjectBinding), typeof(NativeTemplateInvocationQualification), typeof(NativeTemplateProjectAnchor),
            typeof(NativeTemplateCompileRepair), typeof(NativeTemplateCaptureQualification),
        ];
        typeof(NativeTemplatePolicy).Assembly.GetExportedTypes().Where(type => type.Namespace == typeof(NativeTemplatePolicy).Namespace)
            .Should().BeEquivalentTo(expected);
        foreach (Type contract in expected)
        {
            foreach (Type type in contract.GetProperties().Select(property => property.PropertyType)
                .Concat(contract.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))))
            {
                Neutral(type);
            }
        }

        static void Neutral(Type type)
        {
            type.Assembly.GetName().Name.Should().NotBe("func");
            (type.Namespace ?? string.Empty).Should().NotStartWith("Microsoft.TemplateEngine")
                .And.NotBe("Azure.Functions.Cli.Templates.Engine");
            type.Should().NotBe(typeof(IServiceProvider));
            foreach (Type argument in type.GenericTypeArguments)
            {
                Neutral(argument);
            }
        }
    }

    [Fact]
    public void QualificationDefaults_AreOptionalAndReturnedCollectionsAreCopied()
    {
        OriginalEightPolicy policy = new();
        policy.GetRequestedStages(Substitute.For<NativeTemplateSource>()).Should().Be(NativeTemplatePolicyStages.None);
        List<string> diagnostics = [];
        Dictionary<string, string?> defaults = new() { ["Framework"] = "net10.0" };
        List<NativeTemplateSourceStamp> sources = [new("Selected", "body.txt", new('A', 64))];
        NativeTemplateInvocationQualification result = new(true, diagnostics, defaults, sources);

        diagnostics.Add("changed");
        defaults["Framework"] = "changed";
        sources.Clear();

        result.Diagnostics.Should().BeEmpty();
        result.CanonicalDefaults["Framework"].Should().Be("net10.0");
        result.RequiredSources.Should().ContainSingle();
    }

    [Fact]
    public void UnchangedOriginalEightPolicy_LoadsOnExtendedHostAndInheritsUnsupportedDefaults()
    {
        string root = Directory.CreateTempSubdirectory("original-eight-policy-").FullName;
        WorkloadLoadContext? context = null;
        try
        {
            string archive = Path.Combine(root, "original.nupkg");
            using (Stream input = typeof(NativeTemplatePolicyCompatibilityTests).Assembly
                .GetManifestResourceStream("Azure.Functions.Cli.Tests.OriginalEightPolicy.nupkg.bin")!)
            using (FileStream output = File.Create(archive))
            {
                input.CopyTo(output);
            }
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive)))
                .Should().Be("22c509492ed97b4dca1335cb32572a87a56b642563f0802070a4fa1e9a657b3d");
            string install = Path.Combine(root, "installed");
            ZipFile.ExtractToDirectory(archive, install);
            WorkloadMetadata metadata = new WorkloadMetadataReader().Read(install);
            var paths = Substitute.For<IWorkloadPaths>();
            paths.GetInstallDirectory("Original.Eight.Policy", "1.0.0").Returns(install);
            RuntimeWorkloadInfo loaded = new WorkloadLoader(paths).Load([new WorkloadEntry
            {
                PackageId = "Original.Eight.Policy",
                PackageVersion = "1.0.0",
                EntryPoint = metadata.EntryPoint,
            }]).Single();
            context = loaded.LoadContext;
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(loaded.Instance.GetType().Assembly.Location)))
                .Should().Be("55956f2dd22137fec8601aad64dafeae7cde1176997be9b54bc122c04c02220c");
            ServiceCollection services = new();
            services.AddLogging();

            loaded.Instance.Configure(new DefaultFunctionsCliBuilder(services, loaded));
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;

            policy.GetType().GetMethod(nameof(NativeTemplatePolicy.GetRequestedStages))!.DeclaringType.Should().Be(typeof(NativeTemplatePolicy));
            policy.GetRequestedStages(Substitute.For<NativeTemplateSource>()).Should().Be(NativeTemplatePolicyStages.None);
            Action unsupported = () => policy.DescribeProjectLayoutAsync(Substitute.For<NativeTemplateSource>(), CancellationToken.None);
            unsupported.Should().Throw<NotSupportedException>().WithMessage("*Update the owning workload*");
            context.LoadFromAssemblyName(typeof(NativeTemplatePolicy).Assembly.GetName()).Should()
                .BeSameAs(typeof(NativeTemplatePolicy).Assembly);
        }
        finally
        {
            context?.Unload();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // The original policy binary can remain mapped until its collectible context is reclaimed.
            }
        }
    }

    private sealed class OriginalEightPolicy : NativeTemplatePolicy
    {
        public override string Stack => "legacy";
        public override bool Handles(NativeTemplateSource selected) => false;
        public override Task<NativeItemPolicyDescription> DescribeItemAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override Task<IReadOnlyList<string>> ValidateProjectAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override INativeTemplateAppendCapability CreateAppendCapability(NativeTemplateSource selected, WorkingDirectory invocationOutputDirectory)
            => throw new NotSupportedException();
    }
}
