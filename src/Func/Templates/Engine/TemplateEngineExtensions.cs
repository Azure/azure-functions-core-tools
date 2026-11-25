// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.PostActions;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Utils;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Template engine extension methods.
/// </summary>
internal static class TemplateEngineExtensions
{
    extension(Templater templater)
    {
        /// <summary>
        /// Resolves the registered dispatcher and processes the creation result's post actions in declared order.
        /// </summary>
        /// <param name="template">The exact template that produced the creation result.</param>
        /// <param name="creationResult">The result of template creation or its dry run.</param>
        /// <param name="isDryRun">Whether to list actions without executing them.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The post-action execution status.</returns>
        public Task<PostActionExecutionStatus> ProcessPostActionsAsync(
            ITemplateInfo template, ITemplateCreationResult creationResult, bool isDryRun, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(templater);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(creationResult);

            return GetPostActionDispatcher(templater).ProcessAsync(templater, template, creationResult, isDryRun, cancellationToken);
        }

        /// <summary>
        /// Resolves the registered dispatcher and processes the creation result's post actions in declared order.
        /// </summary>
        /// <param name="template">The exact template that produced the creation result.</param>
        /// <param name="creationResult">The result of template creation or its dry run.</param>
        /// <param name="isDryRun">Whether to list actions without executing them.</param>
        /// <param name="allowRunScripts">Whether 'Run script' actions may execute.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The post-action execution status.</returns>
        public Task<PostActionExecutionStatus> ProcessPostActionsAsync(
            ITemplateInfo template,
            ITemplateCreationResult creationResult,
            bool isDryRun,
            AllowRunScripts allowRunScripts,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(templater);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(creationResult);

            return GetPostActionDispatcher(templater)
                .ProcessAsync(templater, template, creationResult, isDryRun, allowRunScripts, cancellationToken);
        }
    }

    private static PostActionDispatcher GetPostActionDispatcher(Templater templater)
    {
        if (!templater.Settings.Components.TryGetComponent(
            PostActionDispatcher.ComponentId, out PostActionDispatcher? dispatcher)
            || dispatcher is null)
        {
            throw new InvalidOperationException(
                "The template post-action dispatcher is not registered. Register templating services with AddTemplating.");
        }

        return dispatcher;
    }

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
