// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

internal interface IBundleResolveTelemetry
{
    public void Record(BundleResolveEvent evt);
}

internal sealed class NullBundleResolveTelemetry : IBundleResolveTelemetry
{
    public static readonly NullBundleResolveTelemetry Instance = new();

    public void Record(BundleResolveEvent evt)
    {
    }
}
