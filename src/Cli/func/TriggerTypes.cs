// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli
{
    public class TriggerTypes
    {
        public const string AzureBlobStorage = "blobtrigger";
        public const string AzureEventHubs = "eventhubtrigger";
        public const string AzureServiceBus = "servicebustrigger";
        public const string AzureStorageQueue = "queuetrigger";
        public const string Kafka = "kafkatrigger";
        public const string RabbitMq = "rabbitmqtrigger";
    }
}
