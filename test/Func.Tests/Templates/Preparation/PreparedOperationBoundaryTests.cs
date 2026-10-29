// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Templates.Preparation;

namespace Azure.Functions.Cli.Tests.Templates.Preparation;

public sealed class PreparedOperationBoundaryTests
{
    [Fact]
    public void PreparedOperationSurface_IsInternalAndHasNoEngineSerializerOrPersistentStoreDependency()
    {
        Type[] contracts =
        [
            typeof(PreparedOperation),
            typeof(PreparedOperationPlan),
            typeof(PreparedOutputEntry),
            typeof(PreparedConfiguration),
            typeof(PreparedOperationIdentity),
            typeof(PreparedOperationCompletion),
        ];
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        string[] forbidden =
        [
            "Microsoft.TemplateEngine",
            "Azure.Functions.Cli.Templates.Engine",
            "System.Text.Json",
            "IConfiguration",
            "TemplateStore",
            "Workload",
            "Quickstart",
        ];

        var surface = contracts.SelectMany(type =>
            type.GetFields(Flags).Select(field => field.FieldType)
                .Concat(type.GetConstructors(Flags).SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)))
                .Concat(type.GetProperties(Flags).Select(property => property.PropertyType))
                .Concat(type.GetMethods(Flags).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))))
            .SelectMany(type => type.GetGenericArguments().Prepend(type));

        contracts.Should().OnlyContain(type => !type.IsPublic);
        surface.Should().NotContain(type => forbidden.Any(part => (type.FullName ?? type.Name).Contains(part, StringComparison.Ordinal)));
        typeof(PreparedOperation).GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Select(parameter => parameter.Name).Should().Equal("plan", "fileSystem", "retainedResource");
        typeof(PreparedOperation).GetMethods(Flags).Select(method => method.Name).Should().NotContain("Render", "Serialize", "Force");
    }
}
