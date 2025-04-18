// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Azure.Functions.Cli.Test.Framework.Helpers
{
    public static class QueueStorageHelper
    {
        public const string StorageEmulatorConnectionString = "UseDevelopmentStorage=true";

        private static QueueClient CreateQueueClient(string queueName)
        {
            var options = new QueueClientOptions
            {
                MessageEncoding = QueueMessageEncoding.Base64
            };

            return new QueueClient(StorageEmulatorConnectionString, queueName, options);
        }

        public static async Task DeleteQueue(string queueName)
        {
            QueueClient queueClient = CreateQueueClient(queueName);
            await queueClient.DeleteIfExistsAsync();
        }

        public static async Task ClearQueue(string queueName)
        {
            QueueClient queueClient = CreateQueueClient(queueName);
            if (await queueClient.ExistsAsync())
            {
                await queueClient.ClearMessagesAsync();
            }
        }

        public static async Task CreateQueue(string queueName)
        {
            QueueClient queueClient = CreateQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();
        }

        public static async Task<string> InsertIntoQueue(string queueName, string queueMessage)
        {
            QueueClient queueClient = CreateQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();
            Response<SendReceipt> response = await queueClient.SendMessageAsync(queueMessage);
            return response.Value.MessageId;
        }

        public static async Task<string> ReadFromQueue(string queueName)
        {
            QueueClient queueClient = CreateQueueClient(queueName);
            QueueMessage? retrievedMessage = null;

            await RetryHelper.RetryAsync(async () =>
            {
                Response<QueueMessage> response = await queueClient.ReceiveMessageAsync();
                retrievedMessage = response.Value;
                return retrievedMessage is not null;
            });

            await queueClient.DeleteMessageAsync(retrievedMessage!.MessageId, retrievedMessage.PopReceipt);
            return retrievedMessage.Body.ToString();
        }

        public static async Task<IEnumerable<string>> ReadMessagesFromQueue(string queueName)
        {
            QueueClient queueClient = CreateQueueClient(queueName);
            QueueMessage[]? retrievedMessages = null;
            var messages = new List<string>();
            await RetryHelper.RetryAsync(async () =>
            {
                retrievedMessages = await queueClient.ReceiveMessagesAsync(maxMessages: 3);
                return retrievedMessages is not null;
            });

            foreach (QueueMessage msg in retrievedMessages!)
            {
                messages.Add(msg.Body.ToString());
                await queueClient.DeleteMessageAsync(msg.MessageId, msg.PopReceipt);
            }

            return messages;
        }
    }
}
