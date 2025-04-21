// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;

namespace Azure.Functions.Cli.Extensions
{
    internal static class UriExtensions
    {
        public static async Task<bool> IsServerRunningAsync(this Uri server)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var rootResponse = await client.GetAsync(server);
                    return rootResponse.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
