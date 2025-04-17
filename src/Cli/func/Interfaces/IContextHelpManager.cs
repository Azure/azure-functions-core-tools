// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Interfaces
{
    public interface IContextHelpManager
    {
        internal string GetTriggerHelp(string triggerName, string language);

        internal Task LoadTriggerHelp(string language, List<string> triggerNames);

        internal bool IsValidTriggerNameForHelp(string triggerName);

        internal bool IsValidTriggerTypeForHelp(string triggerName);

        internal string GetTriggerTypeFromTriggerNameForHelp(string triggerName);
    }
}
