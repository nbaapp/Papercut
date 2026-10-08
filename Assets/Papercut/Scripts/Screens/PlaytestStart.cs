using System;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A one-shot "start the player here" request from the editor: the Sheet Studio's Playtest button writes
    /// it, <see cref="ScreenNavigator.Start"/> consumes it once and starts the player on that Sheet instead of
    /// wherever the Desk scene's Player object is saved (Aaron, 2026-09-24: playtest the sheet being edited).
    /// </summary>
    /// <remarks>
    /// Editor-only by construction: the request lives in <c>UnityEditor.SessionState</c>, which survives the domain
    /// reload of entering Play Mode (the project keeps domain and scene reload on), and in a player build
    /// <see cref="TryConsume"/> is always false. The game never sees who wrote the request or how it is stored;
    /// cutting the feature is deleting this file and the few lines in <see cref="ScreenNavigator"/> that call it.
    /// </remarks>
    public static class PlaytestStart
    {
        /// <summary>Where a playtest starts: a Sheet by grid position and a Front-space point on it (the player's centre) while it is flat.</summary>
        public readonly struct Request
        {
            public readonly Vector2Int GridPosition;
            public readonly Vector2 SheetLocal;

            public Request(Vector2Int gridPosition, Vector2 sheetLocal)
            {
                GridPosition = gridPosition;
                SheetLocal = sheetLocal;
            }

            public override string ToString() => $"Sheet ({GridPosition.x},{GridPosition.y}) at {SheetLocal}";
        }

        /// <summary>
        /// The pending request, if any, and clears it so the next Play Mode session starts normally.
        /// Always false in a player build.
        /// </summary>
        public static bool TryConsume(out Request request)
        {
#if UNITY_EDITOR
            if (IsPending)
            {
                request = new Request(
                    new Vector2Int(UnityEditor.SessionState.GetInt(GridXKey, 0), UnityEditor.SessionState.GetInt(GridYKey, 0)),
                    new Vector2(UnityEditor.SessionState.GetFloat(LocalXKey, 0f), UnityEditor.SessionState.GetFloat(LocalYKey, 0f)));
                Clear();
                return true;
            }
#endif
            request = default;
            return false;
        }

        /// <summary>
        /// Resolves a request against <paramref name="desk"/>: the Sheet at its grid position and the world point
        /// the player's centre goes to, if the sheet exists and <paramref name="hasRoom"/> (the caller's room test,
        /// given the sheet and that world point) allows it. Otherwise false with the reason. Pure apart from the
        /// Desk lookup, which builds its registry from the Desk's children on demand, so it is testable in edit mode.
        /// </summary>
        public static bool TryResolve(Desk desk, in Request request, Func<Sheet, Vector2, bool> hasRoom,
            out Sheet sheet, out Vector2 world, out string reason)
        {
            sheet = null;
            world = default;
            if (desk == null)
            {
                reason = "there is no Desk";
                return false;
            }
            if (!desk.TryGetSheet(request.GridPosition, out sheet))
            {
                reason = $"there is no Sheet ({request.GridPosition.x},{request.GridPosition.y}) on this Desk";
                return false;
            }
            world = sheet.Centre + request.SheetLocal;
            if (hasRoom != null && !hasRoom(sheet, world))
            {
                reason = $"there is no room for the player at {request.SheetLocal} on '{sheet.name}'";
                return false;
            }
            reason = string.Empty;
            return true;
        }

#if UNITY_EDITOR
        const string Prefix = "Papercut.PlaytestStart.";
        const string PendingKey = Prefix + "pending";
        const string GridXKey = Prefix + "gridX";
        const string GridYKey = Prefix + "gridY";
        const string LocalXKey = Prefix + "localX";
        const string LocalYKey = Prefix + "localY";

        /// <summary>True while a request is waiting to be consumed.</summary>
        public static bool IsPending => UnityEditor.SessionState.GetBool(PendingKey, false);

        /// <summary>Writes the request the next Play Mode session's <see cref="ScreenNavigator"/> will honour.</summary>
        public static void Set(in Request request)
        {
            UnityEditor.SessionState.SetInt(GridXKey, request.GridPosition.x);
            UnityEditor.SessionState.SetInt(GridYKey, request.GridPosition.y);
            UnityEditor.SessionState.SetFloat(LocalXKey, request.SheetLocal.x);
            UnityEditor.SessionState.SetFloat(LocalYKey, request.SheetLocal.y);
            UnityEditor.SessionState.SetBool(PendingKey, true);
        }

        /// <summary>Drops any pending request.</summary>
        public static void Clear()
        {
            UnityEditor.SessionState.EraseBool(PendingKey);
            UnityEditor.SessionState.EraseInt(GridXKey);
            UnityEditor.SessionState.EraseInt(GridYKey);
            UnityEditor.SessionState.EraseFloat(LocalXKey);
            UnityEditor.SessionState.EraseFloat(LocalYKey);
        }
#endif
    }
}
