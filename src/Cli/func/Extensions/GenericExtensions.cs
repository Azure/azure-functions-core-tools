// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Extensions
{
    internal static class GenericExtensions
    {
        public static TTarget MergeWith<TTarget, TSource, TSelected>(this TTarget target, TSource source, Func<TSource, TSelected> selector = null)
            where TTarget : class
            where TSource : class
            where TSelected : class
        {
            object selectedSource = null;

            if (source is null && selector is not null)
            {
                selectedSource = selector(source);
            }

            if (selectedSource is null)
            {
                return target;
            }

            foreach (var sourceProperty in selectedSource.GetType().GetProperties())
            {
                var targetProperty = target.GetType().GetProperties().FirstOrDefault(p => p.Name.Equals(sourceProperty.Name, StringComparison.OrdinalIgnoreCase));
                var targetPropertyEnum = target.GetType().GetProperties().FirstOrDefault(p => p.Name.Equals(sourceProperty.Name + "Enum", StringComparison.OrdinalIgnoreCase));
                new List<PropertyInfo>() { targetProperty, targetPropertyEnum }.ForEach(property =>
                {
                    if (property == null)
                    {
                        return;
                    }

                    var st = sourceProperty.PropertyType;
                    var tt = property.PropertyType;

                    Func<bool> validReadableProperties = () => (sourceProperty.CanRead && property.CanRead);
                    Func<bool> typesMatch = () => st == tt;
                    Func<bool> enumMatch = () => tt.IsEnum && Enum.GetUnderlyingType(tt) == st;
                    Func<bool> nullableEnumMatch = () => Nullable.GetUnderlyingType(tt) != null && Nullable.GetUnderlyingType(tt).IsEnum && Enum.GetUnderlyingType(Nullable.GetUnderlyingType(tt)) == st;
                    Func<bool> nullableMatch = () => Nullable.GetUnderlyingType(tt) == st;

                    if (validReadableProperties() && (typesMatch() || nullableMatch() || enumMatch()))
                    {
                        property.SetValue(target, sourceProperty.GetValue(selectedSource));
                    }
                    else if (validReadableProperties() && nullableEnumMatch())
                    {
                        property.SetValue(target, Enum.ToObject(Nullable.GetUnderlyingType(tt), sourceProperty.GetValue(selectedSource)));
                    }
                });
            }

            return target;
        }
    }
}
