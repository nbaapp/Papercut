using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A place on the sheet: a point on the flat (unfolded) sheet in Front-space, and which face of the sheet
    /// it is on. Two things share a surface iff their sides agree and their points lie in the same layer's
    /// <see cref="SheetLayers.Layer.Original"/>. Where that place lies on the Desk right now is the layers' business
    /// (<see cref="SheetPlacement"/>).
    /// </summary>
    public readonly struct SheetPoint
    {
        /// <summary>Front-space coordinates on the flat sheet.</summary>
        public Vector2 Point { get; }

        public SheetFace Side { get; }

        public SheetPoint(Vector2 point, SheetFace side)
        {
            Point = point;
            Side = side;
        }

        public override string ToString() => $"{Point} on {Side}";
    }

    /// <summary>
    /// The pure rules that place movable sheet content (a <see cref="PushableBlock"/>) from the stack of layers a
    /// sheet's folds produce. Such content is a rect on the flat sheet plus a side; where it is on the Desk, which
    /// parts of it are on a visible surface, and how a Desk-space push maps back to the flat sheet all follow from
    /// <see cref="SheetLayers"/>. Managed math only, so it is unit-testable.
    /// </summary>
    /// <remarks>
    /// Vocabulary: a "flat" rect or point is on the unfolded sheet in Front-space (the same space a layer's
    /// <see cref="SheetLayers.Layer.Original"/> is in); a "desk" rect or point is sheet-local, where things lie now.
    /// The <em>glue layer</em> of a rect is the layer whose Original contains the rect's centre: the piece of the
    /// sheet it rests on. A rect may extend past the flat sheet's border only when it hangs past its glue layer's Seam
    /// (it climbed onto a Flap and is not wholly on it); that part is "in the air" above lower layers.
    /// </remarks>
    public static class SheetPlacement
    {
        /// <summary>A polygon of content that is on a visible surface, where it lies (sheet-local).</summary>
        public readonly struct Piece
        {
            public ConvexPolygon Desk { get; }

            /// <summary>The layer the piece's flat coordinates map through (its glue layer, or the layer it was cut onto).</summary>
            public int LayerIndex { get; }

            /// <summary>The layer whose surface the piece is drawn on: <see cref="LayerIndex"/>, or a Flap it is climbing onto.</summary>
            public int SurfaceLayer { get; }

            public Piece(ConvexPolygon desk, int layerIndex, int surfaceLayer)
            {
                Desk = desk;
                LayerIndex = layerIndex;
                SurfaceLayer = surfaceLayer;
            }
        }

        /// <summary>Tolerance for "an isometry maps axes to axes" and "inside the sheet".</summary>
        public const float Tolerance = 1e-4f;

        /// <summary>Index of the layer whose Original contains <paramref name="flatPoint"/> (a point on a crease line goes to the highest such layer); −1 if none.</summary>
        public static int LayerContaining(SheetLayers layers, Vector2 flatPoint)
        {
            for (int i = layers.Layers.Count - 1; i >= 0; i--)
            {
                if (layers.Layers[i].Original.Contains(flatPoint))
                    return i;
            }
            return -1;
        }

        /// <summary>Index of the topmost layer lying over <paramref name="deskPoint"/>; −1 if no part of the sheet is there.</summary>
        public static int TopLayerAt(SheetLayers layers, Vector2 deskPoint)
        {
            for (int i = layers.Layers.Count - 1; i >= 0; i--)
            {
                if (layers.Layers[i].Desk.Contains(deskPoint))
                    return i;
            }
            return -1;
        }

        /// <summary>True if content on <paramref name="side"/> of this layer faces up.</summary>
        public static bool IsUp(in SheetLayers.Layer layer, SheetFace side) => layer.FrontUp == (side == SheetFace.Front);

        /// <summary>The face of this layer that is up.</summary>
        public static SheetFace UpSide(in SheetLayers.Layer layer) => layer.FrontUp ? SheetFace.Front : SheetFace.Back;

        public static bool CentreOn(Vector2 deskPoint, in SheetLayers.Layer layer) => layer.Desk.Contains(deskPoint);

        /// <summary>
        /// Index in <paramref name="stack"/> of the layer that is the same piece of the sheet as <paramref name="layer"/>
        /// (same fold moved it, same flat piece, same pose); −1 if the stack no longer has it whole (a later fold cut or moved it).
        /// </summary>
        public static int SameLayer(SheetLayers stack, in SheetLayers.Layer layer)
        {
            for (int i = 0; i < stack.Layers.Count; i++)
            {
                var candidate = stack.Layers[i];
                if (candidate.MovedBy != layer.MovedBy || candidate.FrontUp != layer.FrontUp || candidate.Original.Count != layer.Original.Count)
                    continue;
                if (Mathf.Abs(candidate.Original.Area - layer.Original.Area) > Tolerance
                    || Vector2.Distance(candidate.ToDesk.T, layer.ToDesk.T) > Tolerance
                    || Vector2.Distance(candidate.ToDesk.Ax, layer.ToDesk.Ax) > Tolerance
                    || !candidate.Original.Contains(layer.Original.Bounds.center))
                    continue;
                return i;
            }
            return -1;
        }

        /// <summary>True if the isometry maps the x and y axes onto axes (every fold isometry does: edge mirrors, 45° swaps and their compositions).</summary>
        public static bool IsAxisAligned(Isometry2D isometry)
            => (Mathf.Abs(isometry.Ax.x) < Tolerance || Mathf.Abs(isometry.Ax.y) < Tolerance)
               && (Mathf.Abs(isometry.Ay.x) < Tolerance || Mathf.Abs(isometry.Ay.y) < Tolerance);

        /// <summary>Where a flat rect lies on the Desk under <paramref name="toDesk"/>: the bounds of its corners (exact when the isometry is axis-aligned).</summary>
        public static Rect DeskRect(Rect flatRect, Isometry2D toDesk) => TransformRect(flatRect, toDesk);

        /// <summary>The flat rect that lies at <paramref name="deskRect"/> under <paramref name="toDesk"/> (the inverse of <see cref="DeskRect"/>).</summary>
        public static Rect FlatRect(Rect deskRect, Isometry2D toDesk) => TransformRect(deskRect, toDesk.Inverse);

        static Rect TransformRect(Rect rect, Isometry2D isometry)
        {
            var a = isometry.Apply(rect.min);
            var b = isometry.Apply(rect.max);
            var c = isometry.Apply(new Vector2(rect.xMin, rect.yMax));
            var d = isometry.Apply(new Vector2(rect.xMax, rect.yMin));
            var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>True if some of the rect is outside the flat sheet (it hangs past a Seam).</summary>
        public static bool OverhangsSheet(Rect flatRect)
        {
            var sheet = FoldGeometry.Sheet;
            return flatRect.xMin < sheet.xMin - Tolerance || flatRect.yMin < sheet.yMin - Tolerance
                   || flatRect.xMax > sheet.xMax + Tolerance || flatRect.yMax > sheet.yMax + Tolerance;
        }

        /// <summary>
        /// How far <paramref name="rect"/> can travel along unit <paramref name="direction"/> before any corner
        /// leaves <paramref name="polygon"/>: the least distance to an edge the motion approaches, 0 if a corner
        /// is already outside such an edge, +∞ if the motion approaches no edge.
        /// </summary>
        public static float MaxTravelInside(Rect rect, Vector2 direction, ConvexPolygon polygon)
        {
            var best = float.PositiveInfinity;
            var corners = new[] { rect.min, rect.max, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMin) };
            for (int e = 0; e < polygon.Count; e++)
            {
                var n = polygon.OutwardNormal(e);
                var along = Vector2.Dot(direction, n);
                if (along <= 1e-6f)
                    continue; // moving parallel to or away from this edge
                foreach (var c in corners)
                    best = Mathf.Min(best, Vector2.Dot(polygon.Vertices[e] - c, n) / along);
            }
            return Mathf.Max(0f, best);
        }

        /// <summary>
        /// Where a push may take content's flat rect: a block with a layer over its pushed part is inside a fold and
        /// stays on that piece of the sheet (a crease is a dead end from inside); a block on the Base stays on the
        /// sheet; a block on top of a Flap may hang past the Flap's Seam (null: only its centre must stay on the sheet).
        /// </summary>
        public static ConvexPolygon PushBound(Rect flatRect, SheetLayers layers, int pushLayer, int glueLayer)
        {
            var pushDesk = DeskRect(flatRect, layers.Layers[pushLayer].ToDesk);
            if (HigherLayerOverlapping(pushDesk, layers, pushLayer) >= 0)
                return layers.Layers[pushLayer].Original;
            return layers.Layers[glueLayer].IsBase ? ConvexPolygon.FromRect(FoldGeometry.Sheet) : null;
        }

        /// <summary>
        /// The Flap that content resting up on its glue layer starts to climb onto by moving from
        /// <paramref name="oldDesk"/> (wholly clear of every layer above the glue layer) to <paramref name="newDesk"/>
        /// (overlapping one); −1 otherwise. Content that is not up on its glue layer (e.g. rolled around a crease onto
        /// the underside) never climbs.
        /// </summary>
        public static int ClimbOnto(Rect oldDesk, Rect newDesk, SheetLayers layers, int glueLayer, SheetFace side)
        {
            if (!IsUp(layers.Layers[glueLayer], side) || HigherLayerOverlapping(oldDesk, layers, glueLayer) >= 0)
                return -1;
            return HigherLayerOverlapping(newDesk, layers, glueLayer);
        }

        /// <summary>The topmost layer above <paramref name="glueLayer"/> that shares area with <paramref name="deskRect"/>; −1 if none.</summary>
        public static int HigherLayerOverlapping(Rect deskRect, SheetLayers layers, int glueLayer)
        {
            for (int i = layers.Layers.Count - 1; i > glueLayer; i--)
            {
                if (layers.Layers[i].Desk.Overlaps(deskRect))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The layer on which content with this flat rect and side is up and present — the one a Desk-space push
        /// of it maps through. The glue layer if it qualifies; otherwise the topmost qualifying layer (a rect cut
        /// by a crease has its visible part on the other side); −1 if it is up nowhere.
        /// </summary>
        public static int PushLayer(Rect flatRect, SheetFace side, SheetLayers layers)
        {
            var glue = LayerContaining(layers, flatRect.center);
            if (glue >= 0 && IsUp(layers.Layers[glue], side))
                return glue;
            var query = ConvexPolygon.FromRect(flatRect);
            for (int i = layers.Layers.Count - 1; i >= 0; i--)
            {
                var layer = layers.Layers[i];
                if (IsUp(layer, side) && layer.Original.Overlaps(query))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The parts of content with this flat rect and side that are on a visible surface, each where it lies now:
        /// the same rule as <see cref="SheetLayers.Coverage"/> (for each layer with that side up, the rect's part on
        /// that layer minus every layer above), keeping the layer per piece; plus the part of the rect that hangs
        /// past the flat sheet (a climbed rect not wholly on its Flap), carried by the glue layer as if it were
        /// part of the sheet. <paramref name="climbTarget"/>, if ≥ 0, is a layer the content is sliding onto: the glue layer's
        /// pieces that lie under it are on top of it instead (drawn on its surface), and only layers above it cover them.
        /// </summary>
        public static List<Piece> VisiblePieces(Rect flatRect, SheetFace side, SheetLayers layers, int climbTarget = -1)
        {
            var pieces = new List<Piece>();
            var stack = layers.Layers;
            var glue = LayerContaining(layers, flatRect.center);
            var onSheet = ConvexPolygon.FromRect(flatRect).ClipToRect(FoldGeometry.Sheet);

            for (int i = 0; i < stack.Count; i++)
            {
                var layer = stack[i];
                if (!IsUp(layer, side) || onSheet.IsEmpty)
                    continue;
                var piece = onSheet.Intersect(layer.Original);
                if (piece.IsEmpty)
                    continue;
                AddCovered(pieces, piece.Transform(layer.ToDesk), i, stack, i == glue ? climbTarget : -1);
            }

            if (glue >= 0 && stack[glue].MovedBy >= 0 && IsUp(stack[glue], side) && OverhangsSheet(flatRect)) // only a Flap has a Seam to hang past
            {
                foreach (var air in ConvexPolygon.FromRect(flatRect).Subtract(ConvexPolygon.FromRect(FoldGeometry.Sheet)))
                {
                    if (!air.IsEmpty)
                        AddCovered(pieces, air.Transform(stack[glue].ToDesk), glue, stack, climbTarget);
                }
            }
            return pieces;
        }

        /// <summary>Adds what is left of <paramref name="desk"/> under the layers above <paramref name="layerIndex"/>; a part over <paramref name="climbTarget"/> sits on that layer's surface instead.</summary>
        static void AddCovered(List<Piece> pieces, ConvexPolygon desk, int layerIndex, IReadOnlyList<SheetLayers.Layer> stack, int climbTarget)
        {
            if (climbTarget > layerIndex)
            {
                var onTop = desk.Intersect(stack[climbTarget].Desk);
                if (!onTop.IsEmpty)
                    AddMinusAbove(pieces, onTop, layerIndex, climbTarget, stack, climbTarget + 1);
                foreach (var rest in desk.Subtract(stack[climbTarget].Desk))
                    AddMinusAbove(pieces, rest, layerIndex, layerIndex, stack, layerIndex + 1, skip: climbTarget);
                return;
            }
            AddMinusAbove(pieces, desk, layerIndex, layerIndex, stack, layerIndex + 1);
        }

        static void AddMinusAbove(List<Piece> pieces, ConvexPolygon desk, int layerIndex, int surface, IReadOnlyList<SheetLayers.Layer> stack, int from, int skip = -1)
        {
            var remaining = new List<ConvexPolygon> { desk };
            for (int j = from; j < stack.Count && remaining.Count > 0; j++)
            {
                if (j == skip)
                    continue;
                var next = new List<ConvexPolygon>();
                foreach (var p in remaining)
                    next.AddRange(p.Subtract(stack[j].Desk));
                remaining = next;
            }
            foreach (var p in remaining)
            {
                if (!p.IsEmpty)
                    pieces.Add(new Piece(p, layerIndex, surface));
            }
        }
    }
}
