// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <inheritdoc cref="IAzureWebJobsStorageClassifier" />
internal sealed class AzureWebJobsStorageClassifier : IAzureWebJobsStorageClassifier
{
    internal const string DevelopmentStorageAccountName = "devstoreaccount1";
    internal const int DefaultBlobPort = 10000;
    internal const int DefaultQueuePort = 10001;
    internal const int DefaultTablePort = 10002;

    private static readonly FrozenSet<string> _literalLocalHosts = new[]
    {
        "localhost",
        "127.0.0.1",
        "::1",
        "[::1]",
        "host.docker.internal",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] _productStyleSubdomains = ["blob", "queue", "table"];

    public AzureWebJobsStorageReference Classify(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return AzureWebJobsStorageReference.NotLocal("Connection string is empty.");
        }

        Dictionary<string, string> parts;
        try
        {
            parts = Parse(connectionString);
        }
        catch (FormatException ex)
        {
            return AzureWebJobsStorageReference.NotLocal($"Connection string could not be parsed: {ex.Message}");
        }

        if (parts.TryGetValue("UseDevelopmentStorage", out string? devValue) &&
            string.Equals(devValue, "true", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.ContainsKey("DevelopmentStorageProxyUri"))
            {
                return AzureWebJobsStorageReference.UserConfigured(
                    endpoints: null,
                    reason: "UseDevelopmentStorage=true with a custom DevelopmentStorageProxyUri.");
            }

            return AzureWebJobsStorageReference.Manageable(
                endpoints: null,
                reason: "UseDevelopmentStorage=true with default Azurite endpoints.");
        }

        bool hasBlob = parts.TryGetValue("BlobEndpoint", out string? blobValue);
        bool hasQueue = parts.TryGetValue("QueueEndpoint", out string? queueValue);
        bool hasTable = parts.TryGetValue("TableEndpoint", out string? tableValue);

        if (!hasBlob && !hasQueue && !hasTable)
        {
            return AzureWebJobsStorageReference.NotLocal(
                "Connection string does not reference local development storage.");
        }

        Uri? blob = TryParseEndpoint(blobValue);
        Uri? queue = TryParseEndpoint(queueValue);
        Uri? table = TryParseEndpoint(tableValue);

        Uri?[] presentEndpoints = [blob, queue, table];
        bool anyParsed = presentEndpoints.Any(static e => e is not null);
        if (!anyParsed)
        {
            return AzureWebJobsStorageReference.NotLocal(
                "Connection string endpoints could not be parsed as URIs.");
        }

        bool anyLocal = presentEndpoints.Any(static e => e is not null && IsLocalHost(e.Host));
        bool anyNonLocal = presentEndpoints.Any(static e => e is not null && !IsLocalHost(e.Host));

        if (!anyLocal)
        {
            return AzureWebJobsStorageReference.NotLocal(
                "Endpoints reference a non-local storage account.");
        }

        if (anyNonLocal)
        {
            return AzureWebJobsStorageReference.NotLocal(
                "Endpoints mix local and non-local hosts; treating as non-local.");
        }

        if (!(hasBlob && hasQueue && hasTable) || blob is null || queue is null || table is null)
        {
            return AzureWebJobsStorageReference.UserConfigured(
                endpoints: null,
                reason: "Local emulator reference is missing one or more of Blob, Queue, or Table endpoints.");
        }

        if (!string.Equals(blob.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(queue.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(table.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            return AzureWebJobsStorageReference.UserConfigured(
                endpoints: BuildTuple(blob, queue, table, accountName: null),
                reason: "Local emulator reference uses HTTPS, which requires user-supplied certificates.");
        }

        if (LooksProductStyle(blob) || LooksProductStyle(queue) || LooksProductStyle(table))
        {
            return AzureWebJobsStorageReference.UserConfigured(
                endpoints: BuildTuple(blob, queue, table, accountName: null),
                reason: "Local emulator reference uses production-style account subdomains.");
        }

        string? accountFromKey = parts.TryGetValue("AccountName", out string? explicitAccount) ? explicitAccount : null;
        string? blobAccount = ExtractPathAccount(blob);
        string? queueAccount = ExtractPathAccount(queue);
        string? tableAccount = ExtractPathAccount(table);

        if (blobAccount is null || queueAccount is null || tableAccount is null)
        {
            return AzureWebJobsStorageReference.UserConfigured(
                endpoints: BuildTuple(blob, queue, table, accountFromKey),
                reason: "Local emulator endpoints do not follow the path-style account format.");
        }

        if (!string.Equals(blobAccount, queueAccount, StringComparison.Ordinal) ||
            !string.Equals(blobAccount, tableAccount, StringComparison.Ordinal))
        {
            return AzureWebJobsStorageReference.UserConfigured(
                endpoints: BuildTuple(blob, queue, table, accountFromKey ?? blobAccount),
                reason: "Local emulator endpoints reference inconsistent account names.");
        }

        if (accountFromKey is not null && !string.Equals(accountFromKey, blobAccount, StringComparison.Ordinal))
        {
            return AzureWebJobsStorageReference.UserConfigured(
                endpoints: BuildTuple(blob, queue, table, accountFromKey),
                reason: "AccountName does not match the account encoded in the local emulator endpoints.");
        }

        AzuriteEndpointTuple tuple = BuildTuple(blob, queue, table, blobAccount);

        if (!string.Equals(blobAccount, DevelopmentStorageAccountName, StringComparison.Ordinal))
        {
            return AzureWebJobsStorageReference.UserConfigured(
                endpoints: tuple,
                reason: $"Local emulator reference uses a custom account name '{blobAccount}'.");
        }

        return AzureWebJobsStorageReference.Manageable(
            endpoints: tuple,
            reason: "Explicit local Azurite endpoints for devstoreaccount1.");
    }

    private static Dictionary<string, string> Parse(string connectionString)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in connectionString.Split(';'))
        {
            string segment = raw.Trim();
            if (segment.Length == 0)
            {
                continue;
            }

            int eq = segment.IndexOf('=');
            if (eq <= 0)
            {
                throw new FormatException($"Segment '{segment}' is not in key=value form.");
            }

            string key = segment[..eq].Trim();
            string value = segment[(eq + 1)..].Trim();
            if (key.Length == 0)
            {
                throw new FormatException("Empty key in connection string segment.");
            }

            result[key] = value;
        }

        return result;
    }

    private static Uri? TryParseEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    internal static bool IsLocalHost(string host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        if (_literalLocalHosts.Contains(host))
        {
            return true;
        }

        return host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksProductStyle(Uri endpoint)
    {
        string host = endpoint.Host;
        int firstDot = host.IndexOf('.');
        if (firstDot <= 0)
        {
            return false;
        }

        string remainder = host[(firstDot + 1)..];
        int nextDot = remainder.IndexOf('.');
        if (nextDot <= 0)
        {
            return false;
        }

        string serviceSegment = remainder[..nextDot];
        return _productStyleSubdomains.Contains(serviceSegment, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ExtractPathAccount(Uri endpoint)
    {
        string path = endpoint.AbsolutePath.Trim('/');
        if (path.Length == 0)
        {
            return null;
        }

        int slash = path.IndexOf('/');
        return slash < 0 ? path : path[..slash];
    }

    private static AzuriteEndpointTuple BuildTuple(Uri blob, Uri queue, Uri table, string? accountName)
        => new(blob, queue, table, accountName ?? string.Empty);
}
