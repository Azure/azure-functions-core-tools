// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using Xunit;
using AbstractionsConstants = Azure.Functions.Cli.Abstractions.Common.Constants;
using FuncConstants = Azure.Functions.Cli.Common.Constants;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

[Collection("FuncHomeEnvVarTests")]
public class WorkloadHomeResolverTests
{
    [Fact]
    public void Resolve_WithWorkloadsHomeSet_PrefersWorkloadsHomeOverFuncHome()
    {
        string workloadsHome = Path.Combine(Path.GetTempPath(), "wl-" + Guid.NewGuid().ToString("N"));
        string funcHome = Path.Combine(Path.GetTempPath(), "fh-" + Guid.NewGuid().ToString("N"));

        string resolved = WithEnv(workloadsHome, funcHome, WorkloadHomeResolver.Resolve);

        Assert.Equal(Path.GetFullPath(workloadsHome), resolved);
    }

    [Fact]
    public void Resolve_WithoutWorkloadsHome_FallsBackToFuncHomeEnvVar()
    {
        string funcHome = Path.Combine(Path.GetTempPath(), "fh-" + Guid.NewGuid().ToString("N"));

        string resolved = WithEnv(null, funcHome, WorkloadHomeResolver.Resolve);

        Assert.Equal(Path.GetFullPath(funcHome), resolved);
    }

    [Fact]
    public void Resolve_WithNeitherEnvVar_FallsBackToUserProfileDefault()
    {
        string expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AbstractionsConstants.FuncHomeDirectoryName));

        string resolved = WithEnv(null, null, WorkloadHomeResolver.Resolve);

        Assert.Equal(expected, resolved);
    }

    private static string WithEnv(string? workloadsHome, string? funcHome, Func<string> action)
    {
        string? previousWorkloads = Environment.GetEnvironmentVariable(FuncConstants.WorkloadsHomeEnvironmentVariable);
        string? previousFunc = Environment.GetEnvironmentVariable(AbstractionsConstants.FuncHomeEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(FuncConstants.WorkloadsHomeEnvironmentVariable, workloadsHome);
            Environment.SetEnvironmentVariable(AbstractionsConstants.FuncHomeEnvironmentVariable, funcHome);
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(FuncConstants.WorkloadsHomeEnvironmentVariable, previousWorkloads);
            Environment.SetEnvironmentVariable(AbstractionsConstants.FuncHomeEnvironmentVariable, previousFunc);
        }
    }
}
