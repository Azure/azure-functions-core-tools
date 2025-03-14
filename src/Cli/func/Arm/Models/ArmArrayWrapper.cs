using System.Collections.Generic;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmArrayWrapper<T>
    {
        public IEnumerable<ArmWrapper<T>> value { get; set; }

        public string nextLink { get; set; }
    }
}