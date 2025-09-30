// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.New
{
    public interface IAction
    {
        public string Name { get; }

        public string Description { get; }

        public Task<int> Run(ParseResult parseResult, CancellationToken cancellationToken);
    }
}
