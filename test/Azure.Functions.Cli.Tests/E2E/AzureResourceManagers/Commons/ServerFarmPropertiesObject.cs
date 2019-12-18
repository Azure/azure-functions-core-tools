namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    public class ServerFarmPropertiesObject
    {
        public int workerSize { get; set; }
        public int workerSizeId { get; set; }
        public int numberOfWorkers { get; set; }
        public int maximumElasticWorkerCount { get; set; }
        public bool reserved { get; set; }
        public string hostingEnvironment { get; set; }
    }
}
