// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections;
using System.Collections.Immutable;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

#pragma warning disable IDE0301 // Collection initialization can be simplified

internal readonly struct EquatableArray<T>(ImmutableArray<T> array)
    : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array = array;

    public int Length => _array.IsDefault ? 0 : _array.Length;

    public T this[int index] => _array[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.IsDefault && other._array.IsDefault)
        {
            return true;
        }

        if (_array.IsDefault || other._array.IsDefault)
        {
            return false;
        }

        if (_array.Length != other._array.Length)
        {
            return false;
        }

        for (int i = 0; i < _array.Length; i++)
        {
            if (!_array[i].Equals(other._array[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefault)
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;
            foreach (T item in _array)
            {
                hash = (hash * 31) + item.GetHashCode();
            }

            return hash;
        }
    }

    public ImmutableArray<T>.Enumerator GetEnumerator()
        => _array.IsDefault ? ImmutableArray<T>.Empty.GetEnumerator() : _array.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
        => _array.IsDefault
            ? ((IEnumerable<T>)ImmutableArray<T>.Empty).GetEnumerator()
            : ((IEnumerable<T>)_array).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => _array.IsDefault
            ? ((IEnumerable)ImmutableArray<T>.Empty).GetEnumerator()
            : ((IEnumerable)_array).GetEnumerator();

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    public static implicit operator ImmutableArray<T>(EquatableArray<T> array)
        => array._array.IsDefault ? ImmutableArray<T>.Empty : array._array;
}
