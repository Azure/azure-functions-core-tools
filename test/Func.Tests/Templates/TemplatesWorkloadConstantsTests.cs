// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Tests.Templates;

public class TemplatesWorkloadConstantsTests
{
    [Theory]
    [InlineData("node", "Azure.Functions.Cli.Workloads.Templates.Node")]
    [InlineData("python", "Azure.Functions.Cli.Workloads.Templates.Python")]
    [InlineData("dotnet", "Azure.Functions.Cli.Workloads.Templates.Dotnet")]
    [InlineData("Node", "Azure.Functions.Cli.Workloads.Templates.Node")]
    [InlineData("  python  ", "Azure.Functions.Cli.Workloads.Templates.Python")]
    public void GetPackageId_Title_Cases_The_Stack(string stack, string expected)
    {
        TemplatesWorkloadConstants.GetPackageId(stack).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetPackageId_Empty_Stack_Throws(string? stack)
    {
        FluentActions.Invoking(() => TemplatesWorkloadConstants.GetPackageId(stack!)).Should().ThrowExactly<ArgumentException>();
    }
}
