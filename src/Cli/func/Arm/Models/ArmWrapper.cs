// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWrapper<T>
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Type { get; set; }

        public string Location { get; set; }

        public string Kind { get; set; }

        public T Properties { get; set; }
    }
}
