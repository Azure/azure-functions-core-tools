﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.ContainerApps.Models
{
    internal class ContainerAppsFunctionDeployResponse
    {
        [JsonProperty(PropertyName = "Code")]
        public string Code { get; set; }

        [JsonProperty(PropertyName = "Details")]
        public IEnumerable<ContainerAppsFunctionDeployResponseDetail> Details { get; set; }

        [JsonProperty(PropertyName = "Innererror")]
        public string Innererror { get; set; }

        [JsonProperty(PropertyName = "Message")]
        public string Message { get; set; }

        [JsonProperty(PropertyName = "Target")]
        public string Target { get; set; }
    }

    internal class ContainerAppsFunctionDeployResponseDetail
    {
        [JsonProperty(PropertyName = "Message")]
        public string Message { get; set; }

        [JsonProperty(PropertyName = "Code")]
        public string Code { get; set; }

        [JsonProperty(PropertyName = "ErrorEntity")]
        public ContainerAppsFunctionDeployResponseErrorEntity ErrorEntity { get; set; }
    }

    internal class ContainerAppsFunctionDeployResponseErrorEntity
    {
        [JsonProperty(PropertyName = "Code")]
        public string Code { get; set; }

        [JsonProperty(PropertyName = "Message")]
        public string Message { get; set; }
    }
}
