using System;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    public enum FunctionAppSku
    {
        Consumption,
        Dedicated,
        ElasticPremium
    }

    public static class FunctionAppSkuExtensions
    {
        public static ServerFarmSkuObject GetServerFarmSku(this FunctionAppSku sku, FunctionAppOs os)
        {
            // Consumption
            if (sku == FunctionAppSku.Consumption)
            {
                return new ServerFarmSkuObject
                {
                    Tier = "Dynamic",
                    Name = "Y1"
                };
            }
            
            // Dedicated
            if (sku == FunctionAppSku.Dedicated && os == FunctionAppOs.Windows)
            {
                return new ServerFarmSkuObject
                {
                    Tier = "Standard",
                    Name = "S1"
                };
            }
            else if (sku == FunctionAppSku.Dedicated && os == FunctionAppOs.Linux)
            {
                return new ServerFarmSkuObject
                {
                    Tier = "PremiumV2",
                    Name = "P1v2"
                };
            }

            // ElasticPremium
            if (sku == FunctionAppSku.ElasticPremium)
            {
                return new ServerFarmSkuObject
                {
                    Tier = "ElasticPremium",
                    Name = "EP1"
                };
            }

            return null;
        }

        public static ServerFarmPropertiesObject GetServerFarmProperties(this FunctionAppSku sku, FunctionAppOs os)
        {
            // Consumption
            if (sku == FunctionAppSku.Consumption)
            {
                return new ServerFarmPropertiesObject
                {
                    workerSize = 0,
                    workerSizeId = 0,
                    numberOfWorkers = 1,
                    maximumElasticWorkerCount = 0,
                    hostingEnvironment = string.Empty,
                    reserved = os == FunctionAppOs.Linux
                };
            }

            // Dedicated
            if (sku == FunctionAppSku.Dedicated)
            {
                return new ServerFarmPropertiesObject
                {
                    workerSize = 0,
                    workerSizeId = 0,
                    numberOfWorkers = 1,
                    maximumElasticWorkerCount = 0,
                    hostingEnvironment = string.Empty,
                    reserved = os == FunctionAppOs.Linux
                };
            }

            // ElasticPremium
            if (sku == FunctionAppSku.ElasticPremium)
            {
                return new ServerFarmPropertiesObject
                {
                    workerSize = 3,
                    workerSizeId = 3,
                    numberOfWorkers = 1,
                    maximumElasticWorkerCount = 20,
                    hostingEnvironment = string.Empty,
                    reserved = os == FunctionAppOs.Linux
                };
            }

            return null;
        }
    }
}
