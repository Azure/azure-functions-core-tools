namespace Azure.Functions.Cli.Common
{
    internal static class ExitCodes
    {
        public const int Success = 0;
        public const int GeneralError = 1;
        public const int MustRunAsAdmin = 2;
        public const int ParseError = 3;
        public const int BuildNativeDepsRequired = 4;
    }
}
