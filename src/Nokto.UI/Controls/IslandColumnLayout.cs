namespace Nokto.UI.Controls;

/// <summary>Reparte secciones contiguas; conserva el orden elegido por el usuario.</summary>
public static class IslandColumnLayout
{
    public static int[] Place(IReadOnlyList<double> heights, int columns)
    {
        if (heights.Count == 0) return [];
        columns = Math.Clamp(columns, 1, heights.Count);
        var result = new int[heights.Count];
        var prefix = new double[heights.Count + 1];
        for (int i = 0; i < heights.Count; i++) prefix[i + 1] = prefix[i] + heights[i] + 8;
        var cost = new double[columns + 1, heights.Count + 1];
        var split = new int[columns + 1, heights.Count + 1];
        for (int c = 0; c <= columns; c++)
            for (int i = 0; i <= heights.Count; i++) cost[c, i] = double.PositiveInfinity;
        cost[0, 0] = 0;
        for (int c = 1; c <= columns; c++)
            for (int end = c; end <= heights.Count; end++)
                for (int start = c - 1; start < end; start++)
                {
                    double candidate = Math.Max(cost[c - 1, start], prefix[end] - prefix[start] - 8);
                    if (candidate >= cost[c, end]) continue;
                    cost[c, end] = candidate; split[c, end] = start;
                }
        int remaining = heights.Count;
        for (int c = columns; c > 0; c--)
        {
            int start = split[c, remaining];
            for (int i = start; i < remaining; i++) result[i] = c - 1;
            remaining = start;
        }
        return result;
    }
}
