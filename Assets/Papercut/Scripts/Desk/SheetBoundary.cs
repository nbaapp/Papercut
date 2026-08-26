using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Walls and exits of a Sheet, rebuilt from the walkable outline (<see cref="WalkableOutline"/>) whenever
    /// the folds change or the sheet becomes/stops being the Screen. Every outline segment gets a solid wall
    /// just outside it; every sheet-edge segment also gets a <see cref="SheetEdge"/> trigger just inside it,
    /// so any walkable ground that reaches an edge — Front, Back, overhang — is an exit toward that direction.
    /// The crease gets a wall only: beyond it the sheet is lifted.
    /// </summary>
    /// <remarks>
    /// Only the Screen has walls and edges (see <see cref="SheetOcclusion"/>). Flat, this produces exactly four
    /// walls and four edge strips. A wall is lengthened past a segment end only where the outline turns a convex
    /// corner there (to close the corner); at a concave corner the two walls already overlap and lengthening
    /// would put a block inside walkable ground.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Sheet), typeof(SheetFolds))]
    public sealed class SheetBoundary : MonoBehaviour
    {
        [SerializeField, Min(0.01f), Tooltip("Thickness of the walls placed outside the walkable area, in sheet units.")]
        float wallThickness = 0.5f;

        [SerializeField, Min(0.05f), Tooltip("How far inside an edge the exit trigger reaches, in sheet units.")]
        float edgeDepth = 0.5f;

        Sheet sheet;
        SheetFolds folds;
        ScreenNavigator navigator;
        readonly List<GameObject> built = new();

        void Awake()
        {
            sheet = GetComponent<Sheet>();
            folds = GetComponent<SheetFolds>();
            navigator = GetComponentInParent<ScreenNavigator>();
            if (navigator == null)
                Debug.LogError($"Sheet '{name}' is not under a Desk with a ScreenNavigator; its edges cannot lead anywhere.", this);
        }

        void OnEnable()
        {
            folds.Changed += Rebuild;
            sheet.PlayerEntered += Rebuild;
            sheet.PlayerLeft += Rebuild;
        }

        void OnDisable()
        {
            folds.Changed -= Rebuild;
            sheet.PlayerEntered -= Rebuild;
            sheet.PlayerLeft -= Rebuild;
            Clear();
        }

        void Start()
        {
            Rebuild();
        }

        void Rebuild()
        {
            Clear();
            if (!sheet.IsScreen)
                return;

            var segments = WalkableOutline.Segments(folds.Folds);
            var t = wallThickness;
            foreach (var segment in segments)
            {
                var dir = (segment.B - segment.A).normalized;
                var extendA = WalkableOutline.IsConvexAt(segments, segment, segment.A) ? t : 0f;
                var extendB = WalkableOutline.IsConvexAt(segments, segment, segment.B) ? t : 0f;
                var centre = segment.Midpoint + segment.Outward * (t * 0.5f) + dir * ((extendB - extendA) * 0.5f);
                AddBox("Wall", dir, centre, new Vector2(segment.Length + extendA + extendB, t), isTrigger: false);

                if (segment.Kind == OutlineKind.SheetEdge)
                {
                    var edge = AddBox($"Edge {segment.Direction}", dir, segment.Midpoint - segment.Outward * (edgeDepth * 0.5f),
                        new Vector2(segment.Length, edgeDepth), isTrigger: true);
                    edge.AddComponent<SheetEdge>().Initialise(sheet, segment.Direction, navigator);
                }
            }
        }

        GameObject AddBox(string label, Vector2 along, Vector2 centreLocal, Vector2 size, bool isTrigger)
        {
            var go = new GameObject(label) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = centreLocal;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg);
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            box.isTrigger = isTrigger;
            built.Add(go);
            return go;
        }

        void Clear()
        {
            foreach (var go in built)
            {
                if (go != null)
                    Destroy(go);
            }
            built.Clear();
        }
    }
}
