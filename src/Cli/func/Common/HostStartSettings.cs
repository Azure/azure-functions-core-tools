// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Common
{
    public class HostStartSettings
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LocalHttpPort { get; set; }

        [JsonProperty("CORS", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Cors { get; set; }

        [JsonProperty("CORSCredentials", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool CorsCredentials { get; set; }
    }
}
