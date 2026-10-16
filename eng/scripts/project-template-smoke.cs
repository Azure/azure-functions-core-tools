// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#:project ../../test/Func.Tests/Func.Tests.csproj
#:property PublishAot=false

using Azure.Functions.Cli.Tests.Templates.Engine;

if (args.Length != 1)
{
    throw new ArgumentException("Supply an empty directory for the generated smoke fixture.");
}

string output = await TemplaterTests.ExportNodeSmokeProjectAsync(Path.GetFullPath(args[0]), CancellationToken.None);
Console.WriteLine(output);