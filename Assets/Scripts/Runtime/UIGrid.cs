using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sizes a <see cref="GridLayoutGroup"/> to the box it has actually been given, and
/// picks the column count while it is there.
///
/// <b>Three screens were doing this and only one of them was doing it well.</b> The
/// arena grid, the level select and the store each had the same twenty lines: measure
/// the parent, divide, take the smaller of what the width allows and what the height
/// allows. Two of the three then held their column count fixed, which is right at
/// exactly one window shape -- three cards across is correct on 16:9 and wasteful on a
/// landscape phone, where the height sets the cell, the cards come out small and the
/// width becomes margin. The third had learned to choose, and the other two could not
/// benefit from it. Same argument as <see cref="HoverCard"/> and <see cref="UIText"/>:
/// the copies had already drifted, and these three screens sit on top of each other.
///
/// A GridLayoutGroup neither clips nor scrolls, so anything that does not fit is simply
/// drawn past the edge of the panel -- which is what cut the descriptions off the bottom
/// row of the dashboard. Fitting both axes is the only thing that cannot do that.
/// </summary>
public static class UIGrid
{
    /// <summary>
    /// Chooses the arrangement with the largest card and writes it to the layout.
    /// </summary>
    /// <param name="layout">The grid to size. Nothing happens if it is null.</param>
    /// <param name="box">The rect the grid has to live inside.</param>
    /// <param name="count">How many children will be placed.</param>
    /// <param name="aspect">Card height as a fraction of its width.</param>
    /// <param name="minCell">Never smaller than this, however tight the box is.</param>
    /// <param name="maxColumns">
    /// A ceiling on how many across. Zero means no ceiling. It exists for the screens
    /// where a wider arrangement wins the arithmetic and loses the argument: a store
    /// card carries three lines of text and a price, so six of them across a handset are
    /// six cards nobody can read, whatever the measurement says about their area.
    /// </param>
    /// <returns>The column count chosen.</returns>
    public static int Fit(GridLayoutGroup layout, RectTransform box, int count,
                          float aspect, float minCell, int maxColumns = 0)
    {
        if (layout == null || box == null) return 1;

        float width = box.rect.width;
        float height = box.rect.height;

        if (width <= 1f || height <= 1f || count <= 0) return Mathf.Max(1, layout.constraintCount);

        int ceiling = maxColumns > 0 ? Mathf.Min(maxColumns, count) : count;

        int columns = 1;
        float cell = 0f;

        for (int tryColumns = 1; tryColumns <= ceiling; tryColumns++)
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)tryColumns));

            float byWidth = (width
                             - layout.padding.left - layout.padding.right
                             - layout.spacing.x * (tryColumns - 1)) / tryColumns;

            float byHeight = (height
                              - layout.padding.top - layout.padding.bottom
                              - layout.spacing.y * (rows - 1)) / rows / Mathf.Max(0.01f, aspect);

            // Strictly greater, so a tie keeps the fewer columns: the same area split
            // into more cards is more cards to read and no more screen to read them on.
            float candidate = Mathf.Min(byWidth, byHeight);
            if (candidate <= cell) continue;

            cell = candidate;
            columns = tryColumns;
        }

        cell = Mathf.Max(minCell, cell);

        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;
        layout.cellSize = new Vector2(cell, cell * aspect);

        // Centred once the height is the limit, so a narrow window leaves an even margin
        // rather than putting all the slack on one side.
        layout.childAlignment = TextAnchor.UpperCenter;

        return columns;
    }
}
