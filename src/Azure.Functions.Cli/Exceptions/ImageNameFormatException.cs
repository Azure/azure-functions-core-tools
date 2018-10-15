using System;

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
