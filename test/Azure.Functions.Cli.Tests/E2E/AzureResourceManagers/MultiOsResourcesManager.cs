using Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers
{
    public class MultiOsResourcesManager<ResourceLabel> : BaseAzureResourceManager
    {
        private HashSet<ResourceLabel> _windowsResources;
        private HashSet<ResourceLabel> _linuxResources;

        public MultiOsResourcesManager()
        {
            _windowsResources = new HashSet<ResourceLabel>();
            _linuxResources = new HashSet<ResourceLabel>();
        }

        protected IEnumerable<ResourceLabel> WindowsResources
        {
            get
            {
                return _windowsResources;
            }
        }

        protected IEnumerable<ResourceLabel> LinuxResources
        {
            get
            {
                return _linuxResources;
            }
        }


        public string GetResourceGroupName(FunctionAppOs os)
        {
            switch (os)
            {
                case FunctionAppOs.Windows:
                    return WindowsResourceGroupName;
                case FunctionAppOs.Linux:
                    return LinuxResourceGroupName;
                default:
                    return string.Empty;
            }
        }

        protected FunctionAppOs GetOsFromResourceLabel(ResourceLabel resource)
        {
            if (_windowsResources.Contains(resource))
            {
                return FunctionAppOs.Windows;
            }

            if (_linuxResources.Contains(resource))
            {
                return FunctionAppOs.Linux;
            }

            throw new Exception($"Resource {resource.ToString()} does not exist when executing GetOsFromResourceLabel");
        }

        protected void AddToResources(ResourceLabel resource, FunctionAppOs os)
        {
            if (os == FunctionAppOs.Windows)
            {
                _windowsResources.Add(resource);
            }

            if (os == FunctionAppOs.Linux)
            {
                _linuxResources.Add(resource);
            }
        }

        protected bool ContainsResource(ResourceLabel resource)
        {
            return _windowsResources.Contains(resource) || _linuxResources.Contains(resource);
        }

        protected void RemoveFromResources(ResourceLabel resource, FunctionAppOs os)
        {
            if (os == FunctionAppOs.Windows)
            {
                _windowsResources.Remove(resource);
            }

            if (os == FunctionAppOs.Linux)
            {
                _linuxResources.Remove(resource);
            }
        }
    }
}
