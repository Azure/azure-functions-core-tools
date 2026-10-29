// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Tests.Templates.Selection;

public sealed class SelectionBoundaryTests
{
    [Fact]
    public void SelectionTypes_AreInternalAndExposeNoNativeOrExecutionContracts()
    {
        Type[] roots =
        [
            typeof(TemplateDescriptor),
            typeof(TemplateArgumentParser),
            typeof(TemplateArgumentParseResult),
            typeof(TemplateSelector),
            typeof(TemplateSelectionRequest),
            typeof(TemplateSelectionResult),
            typeof(TemplateSelectionStatus),
        ];
        string[] forbiddenPrefixes =
        [
            "Microsoft.TemplateEngine.",
            "System.CommandLine.",
            "Azure.Functions.Cli.Projects.",
            "Azure.Functions.Cli.Commands.",
            "Azure.Functions.Cli.Workloads.",
            "Azure.Functions.Cli.Quickstart.",
            "Azure.Functions.Cli.Templates.Prepared",
            "Azure.Functions.Cli.Templates.Package",
            "System.IO.",
        ];
        HashSet<Type> visited = [];
        Queue<Type> pending = new(roots);

        foreach (Type root in roots)
        {
            root.IsNotPublic.Should().BeTrue();
        }

        while (pending.TryDequeue(out Type? type))
        {
            if (!visited.Add(type))
            {
                continue;
            }

            forbiddenPrefixes.Should().NotContain(prefix => (type.FullName ?? "").StartsWith(prefix, StringComparison.Ordinal));
            foreach (Type argument in type.GetGenericArguments())
            {
                pending.Enqueue(argument);
            }

            if (type.Namespace?.StartsWith("Azure.Functions.Cli.Templates", StringComparison.Ordinal) != true)
            {
                continue;
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                pending.Enqueue(property.PropertyType);
            }

            foreach (MethodBase member in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Cast<MethodBase>().Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
            {
                foreach (ParameterInfo parameter in member.GetParameters())
                {
                    pending.Enqueue(parameter.ParameterType);
                }

                if (member is MethodInfo method)
                {
                    pending.Enqueue(method.ReturnType);
                }
            }
        }
    }
}
