﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Common
{
    internal class DiskBacked
    {
        public static DiskBacked<T> Create<T>(string path) where T : new()
        {
            return DiskBacked<T>.Create(path);
        }

        public static DiskBacked<T> CreateEncrypted<T>(string path, string encryptionReason = null) where T : new()
        {
            return DiskBacked<T>.CreateEncrypted(path, encryptionReason);
        }
    }

    internal class DiskBacked<T> where T : new()
    {
        private const string DefaultEncryptionReason = nameof(DiskBacked);
        private readonly string _path;
        private readonly string _encryptionReason;

        private DiskBacked(T value, string path, string encryptionReason = null)
        {
            Value = value;
            _path = path;
            _encryptionReason = encryptionReason;
        }

        public T Value { get; private set; }

        public static DiskBacked<T> Create(string path)
        {
            if (FileSystemHelpers.FileExists(path))
            {
                T value = JsonConvert.DeserializeObject<T>(FileSystemHelpers.ReadAllTextFromFile(path));
                if (value == null)
                {
                    value = new T();
                }

                return new DiskBacked<T>(value, path);
            }

            return new DiskBacked<T>(new T(), path);
        }

        public static DiskBacked<T> CreateEncrypted(string path, string encryptionReason = null)
        {
            encryptionReason = encryptionReason ?? DefaultEncryptionReason;
            if (FileSystemHelpers.FileExists(path))
            {
                var bytes = FileSystemHelpers.ReadAllBytes(path);
                var content = Decrypt(bytes, encryptionReason);
                T value = JsonConvert.DeserializeObject<T>(content);
                return new DiskBacked<T>(value, path, encryptionReason);
            }

            return new DiskBacked<T>(new T(), path, encryptionReason);
        }

        public void Commit()
        {
            if (string.IsNullOrEmpty(_encryptionReason))
            {
                var content = JsonConvert.SerializeObject(Value, Formatting.Indented);
                FileSystemHelpers.WriteAllTextToFile(_path, content);
            }
            else
            {
                var content = JsonConvert.SerializeObject(Value, Formatting.None);
                var bytes = Encrypt(content, nameof(DiskBacked));
                FileSystemHelpers.WriteAllBytes(_path, bytes);
            }
        }

        private static string Decrypt(byte[] value, string reason)
        {
            var bytes = ProtectedData.Unprotect(value, reason);
            return Encoding.UTF8.GetString(bytes);
        }

        private static byte[] Encrypt(string value, string reason)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return ProtectedData.Protect(bytes, reason);
        }
    }
}
