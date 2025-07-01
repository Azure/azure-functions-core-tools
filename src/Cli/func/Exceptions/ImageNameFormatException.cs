// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Exceptions
{
    public class ImageNameFormatException : Exception
    {
        public ImageNameFormatException(string message)
            : base($"{message} cannot be converted in a good image format")
        {
        }
    }
}
