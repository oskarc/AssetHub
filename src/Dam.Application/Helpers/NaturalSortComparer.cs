using System.Text.RegularExpressions;

namespace Dam.Application.Helpers;

/// <summary>
/// Compares strings using natural sort order — numeric segments
/// are compared by value so that "Item 2" sorts before "Item 10".
/// </summary>
public sealed class NaturalSortComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        var xParts = Regex.Split(x, @"(\d+)");
        var yParts = Regex.Split(y, @"(\d+)");

        for (int i = 0; i < Math.Min(xParts.Length, yParts.Length); i++)
        {
            if (int.TryParse(xParts[i], out var xNum) && int.TryParse(yParts[i], out var yNum))
            {
                var numCompare = xNum.CompareTo(yNum);
                if (numCompare != 0) return numCompare;
            }
            else
            {
                var strCompare = string.Compare(xParts[i], yParts[i], StringComparison.OrdinalIgnoreCase);
                if (strCompare != 0) return strCompare;
            }
        }

        return xParts.Length.CompareTo(yParts.Length);
    }
}
