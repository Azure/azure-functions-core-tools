// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Colors.Net;
using Microsoft.WindowsAzure.Storage.Core.Util;

namespace Azure.Functions.Cli.Common
{
    // Simplified https://gist.github.com/DanielSWolf/0ab6a96899cc5377bf54
    internal class SimpleProgressBar : IDisposable
    {
        private readonly int _width;
        private bool _isCompleted = false;
        private int _completed;

        public SimpleProgressBar(string title)
        {
            try
            {
                var bufferWidth = Console.BufferWidth;
                bufferWidth = bufferWidth > 100 ? 100 : bufferWidth;
                _width = bufferWidth - (title.Length + 4);

                ColoredConsole.Write(title);

                ColoredConsole.Write(" [");

                for (var i = 0; i < _width; i++)
                {
                    ColoredConsole.Write('-');
                }

                ColoredConsole.Write("]");

                for (var i = 0; i < _width + 1; i++)
                {
                    ColoredConsole.Write('\b');
                }
            }
            catch
            {
                _isCompleted = true;
                ColoredConsole.WriteLine();
            }
        }

        public void Report(int progressPercentage)
        {
            if (_isCompleted || Console.IsOutputRedirected)
            {
                return;
            }

            var tick = _width * progressPercentage / 100;
            var diff = tick - _completed;
            _completed = tick;
            for (var i = 0; i < diff; i++)
            {
                ColoredConsole.Write('#');
            }

            if (tick == _width)
            {
                ColoredConsole.WriteLine(']');
                _isCompleted = true;
            }
        }

        public void Dispose()
        {
            Report(100);
            _isCompleted = true;
        }
    }

    internal class StorageProgressBar : SimpleProgressBar, IProgress<StorageProgress>
    {
        private readonly long _size;

        public StorageProgressBar(string title, long size)
            : base(title)
        {
            _size = size;
        }

        public void Report(StorageProgress value)
        {
            Report((int)(value.BytesTransferred * 100 / _size));
        }
    }
}
