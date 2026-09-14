using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The Sheet Studio's element palette: the placeable prefabs, discovered from the element folders.
    /// Whatever is in those folders is placeable — curation is moving a prefab out, so cutting a
    /// [TENTATIVE] element (Bible §9) automatically removes it here. Props (Aaron, 2026-09-10) are art-only
    /// scenery with no collision of their own — the Tree — whose collision is laid over them as Wall regions.
    /// </summary>
    public sealed class StudioPalette
    {
        public static readonly string[] ElementFolders =
        {
            "Assets/Papercut/Prefabs/Terrain",
            "Assets/Papercut/Prefabs/Objects",
            "Assets/Papercut/Prefabs/Props",
        };

        readonly List<GameObject> prefabs = new();

        /// <summary>The armed prefab: the next pane click places it. Null when nothing is armed.</summary>
        public GameObject Armed { get; private set; }

        public IReadOnlyList<GameObject> Prefabs => prefabs;

        public void Refresh()
        {
            prefabs.Clear();
            foreach (var folder in ElementFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (prefab != null)
                        prefabs.Add(prefab);
                }
            }
            prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (Armed != null && !prefabs.Contains(Armed))
                Armed = null;
        }

        public void Disarm() => Armed = null;

        /// <summary>Test seam: arm a prefab without going through the strip UI.</summary>
        internal void Arm(GameObject prefab) => Armed = prefab;

        /// <summary>Draws the palette strip; clicking a button arms that prefab (clicking it again disarms).</summary>
        public void Draw(Rect rect)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (prefabs.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        $"No element prefabs found under: {string.Join(", ", ElementFolders)}", MessageType.Info);
                }

                foreach (var prefab in prefabs)
                {
                    var preview = AssetPreview.GetAssetPreview(prefab);
                    var content = preview != null
                        ? new GUIContent(preview, prefab.name)
                        : new GUIContent(prefab.name);
                    var wasArmed = Armed == prefab;
                    var height = rect.height - 22f;
                    var isArmed = GUILayout.Toggle(wasArmed, content, GUI.skin.button,
                        GUILayout.Width(height), GUILayout.Height(height));
                    if (isArmed != wasArmed)
                        Armed = isArmed ? prefab : null;
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(60f)))
                    Refresh();
            }
            GUILayout.Space(2f);
            var label = Armed != null ? $"Armed: {Armed.name} — click a pane to place, Esc/right-click to disarm" : " ";
            GUILayout.Label(label, EditorStyles.miniLabel);
            GUILayout.EndArea();
        }
    }
}
