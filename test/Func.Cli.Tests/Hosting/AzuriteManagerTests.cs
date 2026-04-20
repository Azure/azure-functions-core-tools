// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class AzuriteManagerTests
{
    [Fact]
    public void RequiresAzurite_WithUseDevelopmentStorage_ReturnsTrue()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true"
        };

        Assert.True(AzuriteManager.RequiresAzurite(env));
    }

    [Fact]
    public void RequiresAzurite_WithConnectionString_ReturnsFalse()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AzureWebJobsStorage"] = "DefaultEndpointsProtocol=https;AccountName=test"
        };

        Assert.False(AzuriteManager.RequiresAzurite(env));
    }

    [Fact]
    public void RequiresAzurite_WithNoStorage_ReturnsFalse()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Assert.False(AzuriteManager.RequiresAzurite(env));
    }

    [Fact]
    public void RequiresAzurite_CaseInsensitive()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AzureWebJobsStorage"] = "usedevelopmentstorage=TRUE"
        };

        Assert.True(AzuriteManager.RequiresAzurite(env));
    }
}
