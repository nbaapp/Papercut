using System.Collections.Generic;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// Pure mapping between the folded view (desk space) and authored face space — the single home of the
    /// view→authored inversion, so placement, selection, ghosts, and highlights cannot disagree with each
    /// other or with the renderers' UV math. Exactly the inverse of the composite's mapping:
    /// topmost layer at p → original = ToDesk⁻¹(p); FrontUp → Front at original, else Back at
    /// BackToFront(original) (Bible §6 — the convention Aaron chose for this editor).
    /// </summary>
    public static class StudioFoldMapping
    {
        /// <summary>One visible piece of an element's footprint in the folded view.</summary>
        public readonly struct DeskPiece
        {
            public readonly ConvexPolygon Piece;
            public readonly int LayerIndex;

            /// <summary>True where the element's own face is up in the folded view; face-down pieces draw dimmed.</summary>
            public readonly bool FaceUp;

            public DeskPiece(ConvexPolygon piece, int layerIndex, bool faceUp)
            {
                Piece = piece;
                LayerIndex = layerIndex;
                FaceUp = faceUp;
            }
        }

        /// <summary>The topmost layer whose desk region contains the point; -1 over empty desk.</summary>
        public static int TopLayerAt(SheetLayers layers, Vector2 deskPoint)
        {
            var list = layers.Layers;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Desk.Contains(deskPoint))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Maps a desk point through the topmost layer to its authored face and position. False over empty desk.
        /// </summary>
        public static bool TryMapToAuthored(SheetLayers layers, Vector2 deskPoint, out SheetFace face, out Vector2 authoredLocal)
        {
            var index = TopLayerAt(layers, deskPoint);
            if (index < 0)
            {
                face = SheetFace.Front;
                authoredLocal = default;
                return false;
            }
            MapThroughLayer(layers, index, deskPoint, out face, out authoredLocal);
            return true;
        }

        /// <summary>
        /// Maps a desk point through a specific layer's isometry (extrapolated — the point need not lie on the
        /// layer). Drag rule (plan round-2 N3): the cursor picks the layer; cursor+offset maps through it.
        /// </summary>
        public static void MapThroughLayer(SheetLayers layers, int layerIndex, Vector2 deskPoint, out SheetFace face, out Vector2 authoredLocal)
        {
            var layer = layers.Layers[layerIndex];
            var original = layer.ToDesk.Inverse.Apply(deskPoint);
            if (layer.FrontUp)
            {
                face = SheetFace.Front;
                authoredLocal = original;
            }
            else
            {
                face = SheetFace.Back;
                authoredLocal = SheetGeometry.BackToFront(original);
            }
        }

        /// <summary>
        /// Maps an authored face point forward into desk space through the layer that carries that piece of
        /// the Sheet. False if no layer carries it face-matching (should not happen for a point on the sheet).
        /// </summary>
        public static bool TryMapAuthoredToDesk(SheetLayers layers, SheetFace face, Vector2 authoredLocal, out Vector2 deskPoint, out int layerIndex)
        {
            var flat = face == SheetFace.Front ? authoredLocal : SheetGeometry.BackToFront(authoredLocal);
            var list = layers.Layers;
            // Topmost-first so a piece folded multiple times reports where it currently shows.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!list[i].Original.Contains(flat))
                    continue;
                deskPoint = list[i].ToDesk.Apply(flat);
                layerIndex = i;
                return true;
            }
            deskPoint = default;
            layerIndex = -1;
            return false;
        }

        /// <summary>
        /// Where an element's authored-space rect lies in the folded view, piece by piece: the rect (Back
        /// rects taken through BackToFront into flat sheet space first) intersected with each layer's original
        /// region and transformed by that layer's isometry. Each piece is flagged face-up or face-down
        /// (<c>layer.FrontUp == wantFrontUp</c>, the same orientation test as SheetLayers.Coverage); pieces
        /// under higher layers are still returned — the highlight shows through (stated approximation).
        /// </summary>
        public static void AuthoredBoxToDeskPieces(SheetLayers layers, SheetFace face, Rect authoredRect, List<DeskPiece> results)
            => AuthoredPiecesToDeskPieces(layers, face, new[] { ConvexPolygon.FromRect(authoredRect) }, results);

        /// <summary>
        /// <see cref="AuthoredBoxToDeskPieces"/> for a footprint of several convex pieces (a polygon region):
        /// each authored piece is mapped on its own and the results are pooled.
        /// </summary>
        public static void AuthoredPiecesToDeskPieces(SheetLayers layers, SheetFace face, IReadOnlyList<ConvexPolygon> authoredPieces, List<DeskPiece> results)
        {
            results.Clear();
            var wantFrontUp = face == SheetFace.Front;
            var list = layers.Layers;
            foreach (var authored in authoredPieces)
            {
                var flat = face == SheetFace.Front ? authored : SheetGeometry.BackToFront(authored);
                for (int i = 0; i < list.Count; i++)
                {
                    var piece = list[i].Original.Intersect(flat);
                    if (piece.IsEmpty)
                        continue;
                    results.Add(new DeskPiece(piece.Transform(list[i].ToDesk), i, list[i].FrontUp == wantFrontUp));
                }
            }
        }
    }
}
