// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface ITemplatesManager
    {
        internal Task<IEnumerable<Template>> Templates { get; }

        internal Task<IEnumerable<NewTemplate>> NewTemplates { get; }

        internal Task<IEnumerable<UserPrompt>> UserPrompts { get; }

        internal Task Deploy(string name, string fileName, Template template);

        internal Task Deploy(TemplateJob job, NewTemplate template, IDictionary<string, string> variables);
    }
}
