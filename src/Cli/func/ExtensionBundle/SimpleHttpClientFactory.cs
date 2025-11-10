// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Net;

namespace Azure.Functions.Cli.ExtensionBundle
{
    /// <summary>
    /// Minimal IHttpClientFactory implementation for Core Tools.
    /// Reuses a single HttpClient per logical name to avoid socket exhaustion.
    /// </summary>
    internal class SimpleHttpClientFactory : IHttpClientFactory
    {
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new();
        private static readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(1);

        public HttpClient CreateClient(string name)
        {
            // Name can be ignored for now except for providing isolation if needed later.
            return _clients.GetOrAdd(name ?? string.Empty, static _ =>
            {
                var handler = new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                };

                var client = new HttpClient(handler, disposeHandler: true)
                {
                    Timeout = _defaultTimeout
                };

                client.DefaultRequestHeaders.UserAgent.ParseAdd("azure-functions-core-tools-extension-bundle");
                return client;
            });
        }
    }
}
