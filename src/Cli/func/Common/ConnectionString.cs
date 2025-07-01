// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common
{
    public class ConnectionString
    {
        public string Value { get; set; }

        public string Name { get; set; }

        public string ProviderName { get; set; }
    }
}
