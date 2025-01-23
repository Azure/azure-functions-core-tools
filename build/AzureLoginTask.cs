using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Build
{
    public class AzureLoginTask : Task
    {
        [Required]
        public string DirectoryId { get; set; }

        [Required]
        public string AppId { get; set; }

        [Required]
        public string Key { get; set; }

        public override bool Execute()
        {
            if (string.IsNullOrEmpty(DirectoryId) ||
                string.IsNullOrEmpty(AppId) ||
                string.IsNullOrEmpty(Key))
            {
                Log.LogMessage(MessageImportance.High, "Skipping Azure login - credentials not provided");
                return false;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Shell.Run("cmd", $"/c az login --service-principal -u {DirectoryId} -p \"{Key}\" --tenant {DirectoryId}", silent: true);
            }
            else
            {
                Shell.Run("az", $"login --service-principal -u {AppId} -p \"{Key}\" --tenant {DirectoryId}", silent: true);
            }
            return true;
        }
    }
}
