// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Minimal NuGet-style version-range check for extension-bundle constraints
/// (e.g. <c>[4.0.0, )</c>, <c>[4.18.0, 5.0.0)</c>, <c>4.0.0</c>). Kept
/// dependency-free rather than pulling in NuGet.Versioning transitively;
/// bundle versions are plain dotted numerics with an optional prerelease
/// suffix that is treated as lower than the same release.
/// </summary>
internal readonly struct BundleVersionRange
{
    private readonly BundleVersion _min;
    private readonly BundleVersion? _max;
    private readonly bool _minInclusive;
    private readonly bool _maxInclusive;

    private BundleVersionRange(BundleVersion min, BundleVersion? max, bool minInclusive, bool maxInclusive)
    {
        _min = min;
        _max = max;
        _minInclusive = minInclusive;
        _maxInclusive = maxInclusive;
    }

    /// <summary>
    /// Parses a NuGet interval or a bare minimum version. Returns false for
    /// malformed input so callers can treat an unparseable range as
    /// unsatisfiable rather than throwing.
    /// </summary>
    public static bool TryParse(string? range, out BundleVersionRange result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(range))
        {
            return false;
        }

        string trimmed = range.Trim();
        char first = trimmed[0];
        if (first != '[' && first != '(')
        {
            return BundleVersion.TryParse(trimmed, out BundleVersion? bare)
                && Assign(new BundleVersionRange(bare!.Value, null, minInclusive: true, maxInclusive: false), out result);
        }

        char last = trimmed[^1];
        if (last != ']' && last != ')')
        {
            return false;
        }

        bool minInclusive = first == '[';
        bool maxInclusive = last == ']';
        string inner = trimmed[1..^1];
        string[] parts = inner.Split(',');
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        string minText = parts[0].Trim();
        string maxText = parts.Length == 2 ? parts[1].Trim() : minText;

        if (!BundleVersion.TryParse(minText, out BundleVersion? min))
        {
            return false;
        }

        BundleVersion? max = null;
        if (parts.Length == 2)
        {
            if (maxText.Length > 0 && !BundleVersion.TryParse(maxText, out max))
            {
                return false;
            }
        }
        else
        {
            max = min;
        }

        return Assign(new BundleVersionRange(min!.Value, max, minInclusive, maxInclusive), out result);
    }

    /// <summary>
    /// Returns true when <paramref name="version"/> falls inside this range.
    /// An unparseable version never satisfies the range.
    /// </summary>
    public bool Satisfies(string? version)
    {
        if (!BundleVersion.TryParse(version, out BundleVersion? parsed))
        {
            return false;
        }

        BundleVersion value = parsed!.Value;
        int lower = value.CompareTo(_min);
        if (lower < 0 || (lower == 0 && !_minInclusive))
        {
            return false;
        }

        if (_max is { } max)
        {
            int upper = value.CompareTo(max);
            if (upper > 0 || (upper == 0 && !_maxInclusive))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Assign(BundleVersionRange value, out BundleVersionRange result)
    {
        result = value;
        return true;
    }

    private readonly struct BundleVersion : IComparable<BundleVersion>
    {
        private readonly int[] _parts;
        private readonly bool _hasPrerelease;

        private BundleVersion(int[] parts, bool hasPrerelease)
        {
            _parts = parts;
            _hasPrerelease = hasPrerelease;
        }

        public static bool TryParse(string? text, out BundleVersion? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string release = text.Trim();
            bool hasPrerelease = false;
            int dash = release.IndexOf('-');
            if (dash >= 0)
            {
                hasPrerelease = true;
                release = release[..dash];
            }

            string[] segments = release.Split('.');
            int[] parts = new int[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i]))
                {
                    return false;
                }
            }

            version = new BundleVersion(parts, hasPrerelease);
            return true;
        }

        public int CompareTo(BundleVersion other)
        {
            int length = Math.Max(_parts.Length, other._parts.Length);
            for (int i = 0; i < length; i++)
            {
                int left = i < _parts.Length ? _parts[i] : 0;
                int right = i < other._parts.Length ? other._parts[i] : 0;
                if (left != right)
                {
                    return left.CompareTo(right);
                }
            }

            // A prerelease of the same release sorts below the release.
            return (_hasPrerelease ? 0 : 1).CompareTo(other._hasPrerelease ? 0 : 1);
        }
    }
}
