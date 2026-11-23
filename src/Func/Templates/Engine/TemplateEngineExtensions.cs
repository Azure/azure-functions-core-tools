// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Utils;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Template engine extension methods.
/// </summary>
internal static class TemplateEngineExtensions
{
    extension(IEngineEnvironmentSettings settings)
    {
        /// <summary>
        /// Reads the host configuration for the specified template and returns it as a FuncTemplateHostData instance.
        /// </summary>
        /// <param name="template">The template to read host data for.</param>
        /// <returns>The parsed host configuration as a FuncTemplateHostData instance.</returns>
        /// <exception cref="JsonException">Thrown if the host configuration contains invalid JSON.</exception>
        /// <exception cref="FileNotFoundException">Thrown if the host configuration file is missing.</exception>
        /// <exception cref="IOException">Thrown if the template source cannot be mounted or the host configuration cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown if access to the host configuration is denied.</exception>
        public FuncTemplateHostData ReadHostData(ITemplateInfo template)
        {
            try
            {
                if (template is ITemplateInfoHostJsonCache { HostData: string cachedData }
                    && !string.IsNullOrWhiteSpace(cachedData))
                {
                    return FuncTemplateHostData.Parse(cachedData);
                }

                if (string.IsNullOrEmpty(template.HostConfigPlace))
                {
                    return FuncTemplateHostData.Empty;
                }

                if (!settings.TryGetMountPoint(template.MountPointUri, out IMountPoint? mountPoint) || mountPoint is null)
                {
                    throw new IOException($"Could not mount template source '{template.MountPointUri}'.");
                }

                using (mountPoint)
                {
                    IFile? file = mountPoint.FileInfo(template.HostConfigPlace);
                    if (file is null || !file.Exists)
                    {
                        throw new FileNotFoundException(
                            "The declared host configuration file was not found.", template.HostConfigPlace);
                    }

                    using Stream stream = file.OpenRead();
                    using var reader = new StreamReader(stream);
                    return FuncTemplateHostData.Parse(reader.ReadToEnd());
                }
            }
            catch (JsonException exception)
            {
                throw new JsonException(
                    $"Invalid host configuration for template '{template.Identity}' at '{template.HostConfigPlace}': {exception.Message}", exception);
            }
            catch (FileNotFoundException exception)
            {
                throw new FileNotFoundException(
                    $"Host configuration for template '{template.Identity}' was not found at '{template.MountPointUri}{template.HostConfigPlace}'.",
                    exception.FileName,
                    exception);
            }
            catch (IOException exception)
            {
                throw new IOException(
                    $"Could not read host configuration for template '{template.Identity}' at '{template.HostConfigPlace}'.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new UnauthorizedAccessException(
                    $"Could not read host configuration for template '{template.Identity}' at '{template.HostConfigPlace}'.", exception);
            }
        }
    }
}
