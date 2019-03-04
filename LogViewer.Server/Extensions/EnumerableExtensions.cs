using System;
using System.Collections.Generic;
using System.Linq;
using LogViewer.Server.Models;

namespace LogViewer.Server.Extensions
{
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Returns true if the source contains any of the items in the other list
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <param name="source"></param>
        /// <param name="other"></param>
        /// <returns></returns>
        public static bool ContainsAny<TSource>(this IEnumerable<TSource> source, IEnumerable<TSource> other)
        {
            return other.Any(source.Contains);
        }

        public static IOrderedEnumerable<TSource> OrderBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, SortOrder sortOrder)
        {
            return sortOrder == SortOrder.Ascending ? source.OrderBy(keySelector) : source.OrderByDescending(keySelector);
        }
    }
}
