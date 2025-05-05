// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Helpers
{
    [AttributeUsage(AttributeTargets.Field)]
    internal class DisplayStringAttribute : Attribute
    {
        public DisplayStringAttribute(string value) => Value = value;

        public string Value { get; }
    }
}
