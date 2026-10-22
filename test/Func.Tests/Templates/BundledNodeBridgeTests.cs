// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    // Independent private payload fixture, not a claim about a published bundle/channel.
    private const string NodeBridgeExtensions = """
        {"extensions":[
          {"name":"storage","bindings":["BLOBTRIGGER","queueTrigger"]},
          {"name":"events","bindings":["eventGridTrigger","EventHubTrigger","serviceBusTrigger"]},
          {"name":"cosmos","bindings":["cosmosDBTrigger"]},
          {"name":"durable","bindings":["entityTrigger","orchestrationTrigger"]},
          {"name":"dapr","bindings":["daprPublish","daprServiceInvocationTrigger","daprTopicTrigger"]},
          {"name":"mcp","bindings":["mcpPromptTrigger","mcpResourceTrigger","mcpToolTrigger"]}
        ]}
        """;
}
