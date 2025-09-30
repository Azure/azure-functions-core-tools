// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Abstractions
{
    public class GracefulException : Exception
    {
        public GracefulException()
        {
        }

        public GracefulException(string message)
            : base(message)
        {
            Data.Add(ExceptionExtensions.CLIUserDisplayedException, true);
        }

        public GracefulException(IEnumerable<string> messages, IEnumerable<string>? verboseMessages = null, bool isUserError = true)
            : this(string.Join(Environment.NewLine, messages), isUserError: isUserError)
        {
            if (verboseMessages != null)
            {
                VerboseMessage = string.Join(Environment.NewLine, verboseMessages);
            }
        }

        public GracefulException(string format, params string[] args)
            : this(string.Format(format, args))
        {
        }

        public GracefulException(string message, Exception? innerException = null, bool isUserError = true)
            : base(message, innerException)
        {
            IsUserError = isUserError;
            Data.Add(ExceptionExtensions.CLIUserDisplayedException, isUserError);
        }

        public bool IsUserError { get; } = true;

        public string VerboseMessage { get; } = string.Empty;
    }
}
