using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Build.Tasks
{
    public class AzureLoginTask : Task, ITask
    {
        //When implementing the ITask interface, it is necessary to
        //implement a BuildEngine property of type
        //Microsoft.Build.Framework.IBuildEngine. This is done for
        //you if you derive from the Task class.
        public IBuildEngine BuildEngine { get; set; }

        // When implementing the ITask interface, it is necessary to
        // implement a HostObject property of type object.
        // This is done for you if you derive from the Task class.
        public object HostObject { get; set; }

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
