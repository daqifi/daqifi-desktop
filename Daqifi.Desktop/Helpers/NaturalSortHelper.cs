using System.Globalization;
using System.Text.RegularExpressions;

namespace Daqifi.Desktop.Helpers;

/// <summary>
/// Provides natural sorting functionality for strings containing numeric values.
/// This ensures proper ordering of items like AI0, AI1, AI2, ..., AI10, AI11 instead of alphabetical AI0, AI1, AI10, AI11, AI2.
/// </summary>
public static class NaturalSortHelper
{
    private static readonly Regex NumberRegex = new(@"(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Compares two strings using natural sorting algorithm.
    /// </summary>
    /// <param name="x">First string to compare</param>
    /// <param name="y">Second string to compare</param>
    /// <returns>A signed integer that indicates the relative values of x and y</returns>
    public static int NaturalCompare(string x, string y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        var xParts = NumberRegex.Split(x);
        var yParts = NumberRegex.Split(y);

        int minLength = Math.Min(xParts.Length, yParts.Length);

        for (int i = 0; i < minLength; i++)
        {
            var xPart = xParts[i];
            var yPart = yParts[i];

            // If both parts are numeric, compare as numbers
            if (int.TryParse(xPart, out int xNum) && int.TryParse(yPart, out int yNum))
            {
                int numComparison = xNum.CompareTo(yNum);
                if (numComparison != 0)
                    return numComparison;
            }
            else
            {
                // Compare as strings using culture-invariant comparison
                int stringComparison = string.Compare(xPart, yPart, StringComparison.InvariantCultureIgnoreCase);
                if (stringComparison != 0)
                    return stringComparison;
            }
        }

        // If all compared parts are equal, the shorter string comes first
        return xParts.Length.CompareTo(yParts.Length);
    }

    /// <summary>
    /// Creates a comparer function that can be used with LINQ OrderBy methods for natural sorting.
    /// </summary>
    /// <typeparam name="T">The type of objects to compare</typeparam>
    /// <param name="keySelector">Function to extract the string key from each object</param>
    /// <returns>A comparison function suitable for sorting</returns>
    public static Comparison<T> CreateNaturalComparer<T>(Func<T, string> keySelector)
    {
        return (x, y) => NaturalCompare(keySelector(x), keySelector(y));
    }

    /// <summary>
    /// Sorts an enumerable collection using natural sorting on the specified key.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection</typeparam>
    /// <param name="source">The collection to sort</param>
    /// <param name="keySelector">Function to extract the string key from each element</param>
    /// <returns>A new enumerable with elements sorted in natural order</returns>
    public static IEnumerable<T> NaturalOrderBy<T>(this IEnumerable<T> source, Func<T, string> keySelector)
    {
        return source.OrderBy(keySelector, new NaturalStringComparer());
    }

    /// <summary>
    /// Custom comparer that implements natural string comparison for use with LINQ methods.
    /// </summary>
    private class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            return NaturalCompare(x, y);
        }
    }
}