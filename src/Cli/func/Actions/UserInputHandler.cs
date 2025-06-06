﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.Constants;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions
{
    internal class UserInputHandler : IUserInputHandler
    {
        private readonly Lazy<IEnumerable<UserPrompt>> _userPrompts;
        private readonly IDictionary<string, string> _newTemplateLabelMap;
        private readonly ITemplatesManager _templatesManager;

        public UserInputHandler(ITemplatesManager templatesManager)
        {
            _templatesManager = templatesManager;
            _userPrompts = new Lazy<IEnumerable<UserPrompt>>(() => { return _templatesManager.UserPrompts.Result; });
            _newTemplateLabelMap = CreateLabelMap();
        }

        public void RunUserInputActions(IDictionary<string, string> providedValues, IList<TemplateJobInput> inputs, IDictionary<string, string> variables)
        {
            foreach (var theInput in inputs)
            {
                var userPrompt = _userPrompts.Value.First(x => string.Equals(x.Id, theInput.ParamId, StringComparison.OrdinalIgnoreCase));
                var defaultValue = theInput.DefaultValue ?? userPrompt.DefaultValue;
                string response = null;

                if (providedValues.ContainsKey(theInput.ParamId) && !string.IsNullOrEmpty(providedValues[theInput.ParamId]))
                {
                    response = providedValues[theInput.ParamId];
                }

                if (userPrompt.Value == Constants.UserPromptEnumType || userPrompt.Value == UserPromptBooleanType)
                {
                    var values = new List<string>() { true.ToString(), false.ToString() };
                    if (userPrompt.Value == UserPromptEnumType)
                    {
                        values = userPrompt.EnumList.Select(x => x.Display).ToList();
                    }

                    while (!ValidateResponse(userPrompt, response))
                    {
                        SelectionMenuHelper.DisplaySelectionWizardPrompt(LabelMap(userPrompt.Label));
                        response = SelectionMenuHelper.DisplaySelectionWizard(values);

                        if (string.IsNullOrEmpty(response) && !string.IsNullOrEmpty(defaultValue))
                        {
                            response = defaultValue;
                        }
                        else if (userPrompt.Value == UserPromptEnumType)
                        {
                            response = userPrompt.EnumList.Single(x => x.Display == response).Value;
                        }
                    }
                }
                else
                {
                    while (!ValidateResponse(userPrompt, response))
                    {
                        PrintInputLabel(userPrompt, defaultValue);
                        response = Console.ReadLine();
                        if (string.IsNullOrEmpty(response) && defaultValue != null)
                        {
                            response = defaultValue;
                        }
                    }

                    if (providedValues.ContainsKey(theInput.ParamId))
                    {
                        providedValues[theInput.ParamId] = response;
                    }
                }

                var variableName = theInput.AssignTo;

                variables[variableName] = response;
            }
        }

        public bool ValidateResponse(UserPrompt userPrompt, string response)
        {
            if (response == null)
            {
                return false;
            }

            var validator = userPrompt.Validators?.FirstOrDefault();
            if (validator == null)
            {
                return true;
            }

            var validationRegex = new Regex(validator.Expression);
            var isValid = validationRegex.IsMatch(response);

            if (!isValid && response != string.Empty)
            {
                ColoredConsole.WriteLine(ErrorColor($"{LabelMap(userPrompt.Label)} is not valid."));
            }

            return isValid;
        }

        public void PrintInputLabel(UserPrompt userPrompt, string defaultValue)
        {
            var label = LabelMap(userPrompt.Label);
            ColoredConsole.Write($"{label}: ");
            if (!string.IsNullOrEmpty(defaultValue))
            {
                ColoredConsole.Write($"[{defaultValue}] ");
            }
        }

        private string LabelMap(string label)
        {
            if (!_newTemplateLabelMap.ContainsKey(label))
            {
                return label;
            }

            return _newTemplateLabelMap[label];
        }

        private static IDictionary<string, string> CreateLabelMap()
        {
            return new Dictionary<string, string>
            {
                { "$httpTrigger_route_label", "Route" },
                { "$trigger_functionName_label", "Function Name" },
                { "$app_selected_filename_label", "File Name" },
                { "$httpTrigger_authLevel_label", "Auth Level" },
                { "$queueTrigger_queueName_label", "Queue Name" },
                { "$variables_storageConnStringLabel", "Storage Connection String" },
                { "$cosmosDBIn_connection_label", "CosmosDB Connection String" },
                { "$cosmosDBIn_databaseName_label", "CosmosDB Database Name" },
                { "$cosmosDBIn_containerName_label", "CosmosDB Container Name" },
                { "$cosmosDBIn_leaseContainerName_label", "CosmosDB Lease Container Name" },
                { "$cosmosDBIn_createContainerIfNotExists_label", "Create If Not Exists" },
                { "$eventHubTrigger_connection_label", "EventHub Connection" },
                { "$eventHubOut_path_label", "EventHub Out Path" },
                { "$eventHubTrigger_consumerGroup_label", "EventHub Consumer Group" },
                { "$eventHubTrigger_cardinality_label", "EventHub Cardinality" },
                { "$serviceBusTrigger_connection_label", "Service Bus Connection" },
                { "$serviceBusTrigger_queueName_label", "Service Bus Queue Name" },
                { "$serviceBusTrigger_topicName_label", "Service Bus Topic Name" },
                { "$serviceBusTrigger_subscriptionName_label", "Service Bus Subscripton Name" },
                { "$timerTrigger_schedule_label", "Schedule" },
                { "$blobTrigger_path_label", "Container Path" },
                { "$blobTrigger_connection_label", "Storage Account Connection String" },
                { "$eventGrid_name_label", "EventGrid Name" },
                { "$chat_model_name_label", "Chat Model Name" },
                { "$embeddings_model_name_label", "Embeddings Model Name" },
                { "rag_collection_name_label", "Collection Name" },
                { "$rag_connection_label", "Connection Name" }
            };
        }
    }
}
