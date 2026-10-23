// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;

namespace Azure.Functions.Cli.Tests.Templates.Compatibility;

public sealed class ItemTemplateAdmissionBoundaryTests
{
    [Fact]
    public void RuntimeFactsContracts_ArePublicWorkloadContractsWithoutTemplateEngineTypes()
    {
        Type[] contracts = [typeof(ProjectFactsContext), typeof(ProjectFacts), typeof(ProjectCapability), typeof(ProjectCapabilitySupport)];

        contracts.Should().OnlyContain(type => type.IsPublic && type.Assembly == typeof(FunctionsProject).Assembly);
        contracts.SelectMany(GetSurfaceTypes).Should().NotContain(type => IsForbiddenDependency(type));
        typeof(FunctionsProject).GetMethod(nameof(FunctionsProject.GetProjectFactsAsync))!.IsAbstract.Should().BeFalse();
        typeof(FunctionsProject).GetMethod(nameof(FunctionsProject.GetProjectFactsAsync))!.IsVirtual.Should().BeTrue();
    }

    [Fact]
    public void NeutralAdmissionSurface_HasNoEngineLifecycleGenerationOrPublicationDependencies()
    {
        Type[] contracts =
        [
            typeof(ItemTemplateRequirements),
            typeof(ItemTemplateCompatibility),
            typeof(ItemTemplateCompatibilityDiagnostic),
            typeof(ItemTemplateAdmission),
            typeof(ItemTemplateCompatibilityMatcher),
        ];

        contracts.Should().OnlyContain(type => !type.IsPublic);
        contracts.SelectMany(GetSurfaceTypes).Should().NotContain(type => IsForbiddenDependency(type));
        typeof(ItemTemplateCompatibilityMatcher).GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Should().BeEmpty();
        contracts.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.Name).Should().NotContain(
                ["Commit", "Generate", "Instantiate", "Publish", "Install", "Repair", "Prepare"]);
    }

    private static IEnumerable<Type> GetSurfaceTypes(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetFields(Flags).Select(field => field.FieldType)
            .Concat(type.GetProperties(Flags).Select(property => property.PropertyType))
            .Concat(type.GetConstructors(Flags).SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(type.GetMethods(Flags).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            .SelectMany(ExpandType);
    }

    private static IEnumerable<Type> ExpandType(Type type)
        => type.GetGenericArguments().SelectMany(ExpandType).Prepend(type);

    private static bool IsForbiddenDependency(Type type)
    {
        string name = type.FullName ?? type.Name;
        string[] forbidden =
        [
            "Microsoft.TemplateEngine",
            "Azure.Functions.Cli.Templates.Engine",
            "Azure.Functions.Cli.Workloads",
            "Azure.Functions.Cli.Quickstart",
            "Azure.Functions.Cli.Commands",
            "TemplateStore",
            "Hive",
            "Acquisition",
            "Publisher",
            "Generator",
            "Transaction",
        ];
        return forbidden.Any(part => name.Contains(part, StringComparison.Ordinal));
    }
}
