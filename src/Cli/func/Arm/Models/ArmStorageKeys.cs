// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Arm.Models
{
#pragma warning disable SA1649 // File name should match first type name
    public class ArmStorageKeysArray
#pragma warning restore SA1649 // File name should match first type name
    {
        public ArmStorageKeys[] Keys { get; set; }
    }

    public class ArmStorageKeys
    {
        public string KeyName { get; set; }

        public string Value { get; set; }

        public string Permissions { get; set; }
    }
}
