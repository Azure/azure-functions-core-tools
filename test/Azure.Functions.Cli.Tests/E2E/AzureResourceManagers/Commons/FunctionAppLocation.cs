namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    public enum FunctionAppLocation
    {
        WestUs,
        WestUs2,
        CentralUs,
        EastUs,
        EastAsia,
        SoutheastAsia
    }

    public static class FunctionAppLocationExtensions
    {
        public static string ToRegion(this FunctionAppLocation location) {
            return location.ToString().ToLower();
        }
    }
}
