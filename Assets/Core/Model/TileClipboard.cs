using System;
using System.Collections.Generic;
using System.Linq;

namespace Jacquard {

// Copies keep their shape on the plane. Flow tiles have no independent copy;
// the existing Tile.Copy contract excludes them from both clipboard paths.
public sealed class TileClipboard
{
    readonly List<(GridPoint offset, Tile tile)> _tiles = new();
    public int Count => _tiles.Count;

    public void Copy(Score score, IEnumerable<Tile> selection)
    {
        _tiles.Clear();
        var cells = selection.Distinct().Select(tile => (point: score.Locate(tile), tile))
            .Where(cell => cell.point.HasValue && cell.tile.Copy() != null).ToList();
        if (cells.Count == 0) return;
        var x = cells.Min(cell => cell.point.Value.X);
        var y = cells.Min(cell => cell.point.Value.Y);
        foreach (var cell in cells.OrderBy(cell => cell.point.Value.Y)
                                  .ThenBy(cell => cell.point.Value.X))
            _tiles.Add((cell.point.Value.Offset(-x, -y), cell.tile.Copy()));
    }

    // Validate against a shadow score first. Growth at a terminator and space
    // occupied by earlier pasted cells both participate in the same checks as
    // the real edit, so a refused group never leaves half of itself behind.
    public bool Paste(Score score, GridPoint origin, List<Tile> pasted = null)
    {
        if (_tiles.Count == 0) return false;
        var shadow = new Score();
        foreach (var lane in score.Lanes)
        {
            var copy = shadow.AddLane(lane.X, lane.Y, lane.Head, lane.Steps.Count);
            for (var i = 0; i < lane.Steps.Count; i++)
                copy.Steps[i].Tiles.AddRange(lane.Steps[i].Tiles);
        }
        foreach (var cell in _tiles)
            if (!shadow.PlaceStack(origin.Offset(cell.offset.X, cell.offset.Y),
                                   new[] { cell.tile })) return false;
        foreach (var cell in _tiles)
        {
            var tile = cell.tile.Copy();
            score.PlaceStack(origin.Offset(cell.offset.X, cell.offset.Y), new[] { tile });
            pasted?.Add(tile);
        }
        return true;
    }
}

} // namespace Jacquard
