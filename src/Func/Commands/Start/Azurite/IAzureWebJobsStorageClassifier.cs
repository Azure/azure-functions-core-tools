// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Classifies an <c>AzureWebJobsStorage</c> connection string into the
/// categories the managed-Azurite feature reasons over.
/// </summary>
internal interface IAzureWebJobsStorageClassifier
{
    /// <summary>
    /// Classifies <paramref name="connectionString"/>. Null, empty, or
    /// whitespace input is treated as <see cref="AzureWebJobsStorageClassification.NotLocal"/>.
    /// The classifier is pure: it never performs I/O.
    /// </summary>
    public AzureWebJobsStorageReference Classify(string? connectionString);
}
