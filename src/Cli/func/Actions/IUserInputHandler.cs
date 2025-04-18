// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Actions
{
    internal interface IUserInputHandler
    {
        public void RunUserInputActions(IDictionary<string, string> providedValues, IList<TemplateJobInput> inputs, IDictionary<string, string> variables);

        public bool ValidateResponse(UserPrompt userPrompt, string response);

        public void PrintInputLabel(UserPrompt userPrompt, string defaultValue);
    }
}
