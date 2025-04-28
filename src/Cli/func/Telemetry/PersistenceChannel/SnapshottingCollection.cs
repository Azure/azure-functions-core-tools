// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections;
using System.Diagnostics;

namespace Azure.Functions.Cli.Telemetry.PersistenceChannel
{
    internal abstract class SnapshottingCollection<TItem, TCollection> : ICollection<TItem>
        where TCollection : class, ICollection<TItem>
    {
#pragma warning disable SA1401 // Fields should be private
#pragma warning disable SA1306 // Field names should begin with lower-case letter
        protected readonly TCollection Collection;

        protected TCollection Snapshot;
#pragma warning restore SA1306 // Field names should begin with lower-case letter
#pragma warning restore SA1401 // Fields should be private

        protected SnapshottingCollection(TCollection collection)
        {
            Debug.Assert(collection != null, "collection");
            Collection = collection;
        }

        public int Count => GetSnapshot().Count;

        public bool IsReadOnly => false;

        public void Add(TItem item)
        {
            lock (Collection)
            {
                Collection.Add(item);
                Snapshot = default(TCollection);
            }
        }

        public void Clear()
        {
            lock (Collection)
            {
                Collection.Clear();
                Snapshot = default(TCollection);
            }
        }

        public bool Contains(TItem item)
        {
            return GetSnapshot().Contains(item);
        }

        public void CopyTo(TItem[] array, int arrayIndex)
        {
            GetSnapshot().CopyTo(array, arrayIndex);
        }

        public bool Remove(TItem item)
        {
            lock (Collection)
            {
                bool removed = Collection.Remove(item);
                if (removed)
                {
                    Snapshot = default(TCollection);
                }

                return removed;
            }
        }

        public IEnumerator<TItem> GetEnumerator()
        {
            return GetSnapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        protected abstract TCollection CreateSnapshot(TCollection collection);

        protected TCollection GetSnapshot()
        {
            TCollection localSnapshot = Snapshot;
            if (localSnapshot == null)
            {
                lock (Collection)
                {
                    Snapshot = CreateSnapshot(Collection);
                    localSnapshot = Snapshot;
                }
            }

            return localSnapshot;
        }
    }
}
