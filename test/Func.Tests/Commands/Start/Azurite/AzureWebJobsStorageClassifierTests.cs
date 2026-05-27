// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzureWebJobsStorageClassifierTests
{
    private const string DevAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private readonly AzureWebJobsStorageClassifier _classifier = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NullOrWhitespace_ReturnsNotLocal(string? value)
    {
        var result = _classifier.Classify(value);

        Assert.Equal(AzureWebJobsStorageClassification.NotLocal, result.Classification);
        Assert.Null(result.Endpoints);
    }

    [Fact]
    public void Classify_UseDevelopmentStorageTrue_IsManageable()
    {
        var result = _classifier.Classify("UseDevelopmentStorage=true");

        Assert.Equal(AzureWebJobsStorageClassification.ManageableAzurite, result.Classification);
        Assert.Null(result.Endpoints);
    }

    [Fact]
    public void Classify_UseDevelopmentStorageTrue_KeyIsCaseInsensitive()
    {
        var result = _classifier.Classify("usedevelopmentstorage=TRUE");

        Assert.Equal(AzureWebJobsStorageClassification.ManageableAzurite, result.Classification);
    }

    [Fact]
    public void Classify_UseDevelopmentStorageWithProxy_IsUserConfigured()
    {
        var result = _classifier.Classify(
            "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://127.0.0.1");

        Assert.Equal(AzureWebJobsStorageClassification.UserConfiguredAzurite, result.Classification);
    }

    [Fact]
    public void Classify_ExplicitDefaultAzuriteEndpoints_IsManageable()
    {
        var cs = $"DefaultEndpointsProtocol=http;" +
            $"AccountName=devstoreaccount1;" +
            $"AccountKey={DevAccountKey};" +
            "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;" +
            "QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;" +
            "TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.ManageableAzurite, result.Classification);
        Assert.NotNull(result.Endpoints);
        Assert.Equal("devstoreaccount1", result.Endpoints!.AccountName);
        Assert.Equal(10000, result.Endpoints.BlobEndpoint.Port);
        Assert.Equal(10001, result.Endpoints.QueueEndpoint.Port);
        Assert.Equal(10002, result.Endpoints.TableEndpoint.Port);
    }

    [Fact]
    public void Classify_CustomLocalPortsForDevStoreAccount_IsManageable()
    {
        var cs = "DefaultEndpointsProtocol=http;" +
            "AccountName=devstoreaccount1;" +
            $"AccountKey={DevAccountKey};" +
            "BlobEndpoint=http://127.0.0.1:20000/devstoreaccount1;" +
            "QueueEndpoint=http://127.0.0.1:20001/devstoreaccount1;" +
            "TableEndpoint=http://127.0.0.1:20002/devstoreaccount1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.ManageableAzurite, result.Classification);
        Assert.Equal(20000, result.Endpoints!.BlobEndpoint.Port);
    }

    [Fact]
    public void Classify_HttpsLocalEndpoints_IsUserConfigured()
    {
        var cs = "DefaultEndpointsProtocol=https;" +
            "AccountName=devstoreaccount1;" +
            $"AccountKey={DevAccountKey};" +
            "BlobEndpoint=https://localhost:10000/devstoreaccount1;" +
            "QueueEndpoint=https://localhost:10001/devstoreaccount1;" +
            "TableEndpoint=https://localhost:10002/devstoreaccount1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.UserConfiguredAzurite, result.Classification);
    }

    [Fact]
    public void Classify_CustomAccountNameLocalEndpoints_IsUserConfigured()
    {
        var cs = "DefaultEndpointsProtocol=http;" +
            "AccountName=account1;" +
            $"AccountKey={DevAccountKey};" +
            "BlobEndpoint=http://127.0.0.1:10000/account1;" +
            "QueueEndpoint=http://127.0.0.1:10001/account1;" +
            "TableEndpoint=http://127.0.0.1:10002/account1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.UserConfiguredAzurite, result.Classification);
        Assert.NotNull(result.Endpoints);
        Assert.Equal("account1", result.Endpoints!.AccountName);
    }

    [Fact]
    public void Classify_ProductStyleLocalhost_IsUserConfigured()
    {
        var cs = "DefaultEndpointsProtocol=http;" +
            "AccountName=account1;" +
            $"AccountKey={DevAccountKey};" +
            "BlobEndpoint=http://account1.blob.localhost:10000;" +
            "QueueEndpoint=http://account1.queue.localhost:10001;" +
            "TableEndpoint=http://account1.table.localhost:10002;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.UserConfiguredAzurite, result.Classification);
    }

    [Fact]
    public void Classify_PartialLocalEndpoints_IsUserConfigured()
    {
        var cs = "DefaultEndpointsProtocol=http;" +
            "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;" +
            "QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.UserConfiguredAzurite, result.Classification);
        Assert.Null(result.Endpoints);
    }

    [Fact]
    public void Classify_AzureCloudConnectionString_IsNotLocal()
    {
        var cs = $"DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey={DevAccountKey};" +
            "EndpointSuffix=core.windows.net";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.NotLocal, result.Classification);
    }

    [Fact]
    public void Classify_SasConnectionString_IsNotLocal()
    {
        var cs = "BlobEndpoint=https://myaccount.blob.core.windows.net;SharedAccessSignature=sv=2024";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.NotLocal, result.Classification);
    }

    [Fact]
    public void Classify_ExplicitCloudEndpoints_IsNotLocal()
    {
        var cs = "DefaultEndpointsProtocol=https;" +
            $"AccountName=myaccount;AccountKey={DevAccountKey};" +
            "BlobEndpoint=https://myaccount.blob.core.windows.net;" +
            "QueueEndpoint=https://myaccount.queue.core.windows.net;" +
            "TableEndpoint=https://myaccount.table.core.windows.net;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.NotLocal, result.Classification);
    }

    [Fact]
    public void Classify_MixedLocalAndCloudEndpoints_IsNotLocal()
    {
        var cs = "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;" +
            "QueueEndpoint=https://myaccount.queue.core.windows.net;" +
            "TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.NotLocal, result.Classification);
    }

    [Fact]
    public void Classify_KeysAreCaseInsensitive()
    {
        var cs = "BLOBENDPOINT=http://127.0.0.1:10000/devstoreaccount1;" +
            "queueendpoint=http://127.0.0.1:10001/devstoreaccount1;" +
            "TableEndPoint=http://127.0.0.1:10002/devstoreaccount1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.ManageableAzurite, result.Classification);
    }

    [Fact]
    public void Classify_ExtraSemicolonsAndWhitespace_AreTolerated()
    {
        var cs = ";; BlobEndpoint = http://127.0.0.1:10000/devstoreaccount1 ;; " +
            " QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1; " +
            "TableEndpoint=http://127.0.0.1:10002/devstoreaccount1 ; ;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.ManageableAzurite, result.Classification);
        Assert.Equal("devstoreaccount1", result.Endpoints!.AccountName);
    }

    [Fact]
    public void Classify_AccountKeyContainingEqualsSigns_ParsesCorrectly()
    {
        var cs = $"AccountName=devstoreaccount1;AccountKey={DevAccountKey};" +
            "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;" +
            "QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;" +
            "TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.ManageableAzurite, result.Classification);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("host.docker.internal")]
    [InlineData("foo.localhost")]
    [InlineData("account1.blob.localhost")]
    public void IsLocalHost_RecognizesLocalHosts(string host)
        => Assert.True(AzureWebJobsStorageClassifier.IsLocalHost(host));

    [Theory]
    [InlineData("myaccount.blob.core.windows.net")]
    [InlineData("example.com")]
    [InlineData("")]
    [InlineData("10.0.0.1")]
    public void IsLocalHost_RejectsNonLocalHosts(string host)
        => Assert.False(AzureWebJobsStorageClassifier.IsLocalHost(host));

    [Fact]
    public void Classify_LocalhostByName_IsManageable()
    {
        var cs = "BlobEndpoint=http://localhost:10000/devstoreaccount1;" +
            "QueueEndpoint=http://localhost:10001/devstoreaccount1;" +
            "TableEndpoint=http://localhost:10002/devstoreaccount1;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.ManageableAzurite, result.Classification);
    }

    [Fact]
    public void Classify_AccountNameMismatchWithEndpointPath_IsUserConfigured()
    {
        var cs = "AccountName=devstoreaccount1;" +
            "BlobEndpoint=http://127.0.0.1:10000/otheraccount;" +
            "QueueEndpoint=http://127.0.0.1:10001/otheraccount;" +
            "TableEndpoint=http://127.0.0.1:10002/otheraccount;";

        var result = _classifier.Classify(cs);

        Assert.Equal(AzureWebJobsStorageClassification.UserConfiguredAzurite, result.Classification);
    }

    [Fact]
    public void Classify_MalformedSegment_IsNotLocal()
    {
        var result = _classifier.Classify("garbage-without-equals");

        Assert.Equal(AzureWebJobsStorageClassification.NotLocal, result.Classification);
    }
}
