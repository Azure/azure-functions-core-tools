namespace Azure.Functions.Cli.Arm.Models
{
    public class ArmStorageKeysArray
    {
        public ArmStorageKeys[] keys { get; set; }
    }

    public class ArmStorageKeys
    {
        public string keyName { get; set; }
        public string value { get; set; }
        public string permissions { get; set; }
    }
}