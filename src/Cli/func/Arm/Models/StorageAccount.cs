// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Arm.Models
{
    public class StorageAccount
    {
        public string StorageAccountName { get; set; }

        public string StorageAccountKey { get; set; }

        public string ConnectionString
            => string.Format(
                Constants.StorageConnectionStringTemplate,
                StorageAccountName,
                StorageAccountKey);
    }
}
