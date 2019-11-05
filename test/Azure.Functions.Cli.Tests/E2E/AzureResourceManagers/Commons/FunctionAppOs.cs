using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    public enum FunctionAppOs
    {
        Windows,
        Linux
    }

    public static class FunctionAppOsExtensions
    {
        public static string GetFunctionAppKindLabel(this FunctionAppOs os)
        {
            switch (os)
            {
                case FunctionAppOs.Windows:
                    return "functionapp";
                case FunctionAppOs.Linux:
                    return "functionapp,linux";
                default:
                    return string.Empty;
            }
        }
        public static string GetServerFarmKindLabel(this FunctionAppOs os)
        {
            switch (os)
            {
                case FunctionAppOs.Windows:
                    return "windows";
                case FunctionAppOs.Linux:
                    return "linux";
                default:
                    return string.Empty;
            }
        }
    }
}
