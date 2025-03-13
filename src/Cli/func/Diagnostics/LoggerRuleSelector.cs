// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// This file is modified copy of: 
// https://github.com/aspnet/Logging/blob/cc350d7ef616ef292c1b4ae7130b8c2b45fc1164/src/Microsoft.Extensions.Logging/LoggerRuleSelector.cs.

using System;

namespace Microsoft.Extensions.Logging
{
    internal class LoggerRuleSelector
    {
        public void Select(LoggerFilterOptions options, Type providerType, string category, out LogLevel? minLevel, out Func<string, string, LogLevel, bool> filter)
        {
            filter = null;
            minLevel = options.MinLevel;

            // Filter rule selection:
            // 1. Select rules for current logger type, if there is none, select ones without logger type specified
            // 2. Select rules with longest matching categories
            // 3. If there nothing matched by category take all rules without category
            // 3. If there is only one rule use it's level and filter
            // 4. If there are multiple rules use last
            // 5. If there are no applicable rules use global minimal level

            LoggerFilterRule current = null;
            foreach (var rule in options.Rules)
            {
                if (IsBetter(rule, current, null, category))
                {
                    current = rule;
                }
            }

            if (current != null)
            {
                filter = current.Filter;
                minLevel = current.LogLevel;
            }
        }

        private static bool IsBetter(LoggerFilterRule rule, LoggerFilterRule current, string logger, string category)
        {
            // Skip rules with inapplicable type or category
            if (rule.ProviderName != null && rule.ProviderName != logger)
            {
                return false;
            }

            if (rule.CategoryName != null && !category.StartsWith(rule.CategoryName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (current?.ProviderName != null)
            {
                if (rule.ProviderName == null)
                {
                    return false;
                }
            }
            else
            {
                // We want to skip category check when going from no provider to having provider
                if (rule.ProviderName != null)
                {
                    return true;
                }
            }

            if (current?.CategoryName != null)
            {
                if (rule.CategoryName == null)
                {
                    return false;
                }

                if (current.CategoryName.Length > rule.CategoryName.Length)
                {
                    return false;
                }
            }

            return true;
        }
    }
}