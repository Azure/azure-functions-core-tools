namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    public enum FunctionAppRuntime
    {
        DotNet,
        PowerShell,
        Java,
        Node
    }

    public static class FunctionAppRuntimeExtensions
    {
        public static string ToFunctionWorkerRuntime(this FunctionAppRuntime runtime)
        {
            return runtime.ToString().ToLower();
        }
    }
}
