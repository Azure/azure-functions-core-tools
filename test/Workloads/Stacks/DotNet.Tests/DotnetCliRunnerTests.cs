// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.DotNet;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class DotnetCliRunnerTests
{
    [Fact]
    public void CreateStartInfo_SuppressesDotnetFirstRunSideEffectsForChildOperations()
    {
        IDotnetPathResolver pathResolver = Substitute.For<IDotnetPathResolver>();
        pathResolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(pathResolver);

        System.Diagnostics.ProcessStartInfo startInfo = runner.CreateStartInfo(["msbuild"], null);

        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"].Should().Be("1");
        startInfo.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"].Should().Be("false");
    }
}
