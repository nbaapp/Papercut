using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// Wiring between plates and what their effects act on, for the Studio's link mode. Target slots are
    /// discovered generically — every serialized GameObject-reference field, single or array, on every
    /// <see cref="PlateEffect"/> under an element — so a future effect kind gets Studio wiring with no editor
    /// change. A list slot (<see cref="SwitchPresenceEffect"/>'s targets) holds any number of targets, so one
    /// plate wires to several gates (Aaron, 2026-09-21).
    /// </summary>
    public static class StudioLinks
    {
        /// <summary>One wireable target field on one effect component: a single reference or a list of them.</summary>
        public readonly struct Slot
        {
            public readonly PlateEffect Effect;
            public readonly string PropertyPath;
            public readonly string DisplayName;
            /// <summary>The wired targets: zero or one for a single slot, any number for a list slot. Nulls are skipped.</summary>
            public readonly IReadOnlyList<GameObject> Targets;
            public readonly bool IsList;

            public Slot(PlateEffect effect, string propertyPath, string displayName, IReadOnlyList<GameObject> targets, bool isList)
            {
                Effect = effect;
                PropertyPath = propertyPath;
                DisplayName = displayName;
                Targets = targets;
                IsList = isList;
            }

            public bool Contains(GameObject target)
            {
                if (target == null)
                    return false;
                foreach (var t in Targets)
                {
                    if (t == target)
                        return true;
                }
                return false;
            }
        }

        const string GameObjectReferenceType = "PPtr<$GameObject>";

        /// <summary>All wireable slots of an element, in component order. Empty for non-plates.</summary>
        public static List<Slot> GetSlots(GameObject element)
        {
            var slots = new List<Slot>();
            if (element == null)
                return slots;

            foreach (var effect in element.GetComponentsInChildren<PlateEffect>(true))
            {
                var serialized = new SerializedObject(effect);
                var property = serialized.GetIterator();
                for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
                {
                    var displayName = $"{ObjectNames.NicifyVariableName(effect.GetType().Name)} → {property.displayName}";
                    if (property.isArray && property.arrayElementType == GameObjectReferenceType)
                    {
                        var targets = new List<GameObject>(property.arraySize);
                        for (int i = 0; i < property.arraySize; i++)
                        {
                            if (property.GetArrayElementAtIndex(i).objectReferenceValue is GameObject target)
                                targets.Add(target);
                        }
                        slots.Add(new Slot(effect, property.propertyPath, displayName, targets, isList: true));
                    }
                    else if (property.propertyType == SerializedPropertyType.ObjectReference && property.type == GameObjectReferenceType)
                    {
                        var targets = new List<GameObject>(1);
                        if (property.objectReferenceValue is GameObject target)
                            targets.Add(target);
                        slots.Add(new Slot(effect, property.propertyPath, displayName, targets, isList: false));
                    }
                }
            }
            return slots;
        }

        public static bool HasSlots(GameObject element) => GetSlots(element).Count > 0;

        /// <summary>
        /// Wires <paramref name="target"/> into a slot: a list slot gains it (a repeat is a no-op — it is what a
        /// second click on the same thing means), a single slot is set to it. Undoable — goes through
        /// SerializedObject, the same path as an Inspector edit. Every list edit also drops null entries (a
        /// target destroyed outside the Studio), so a stale reference never survives the next wiring.
        /// </summary>
        public static void Wire(in Slot slot, GameObject target)
        {
            if (target == null)
            {
                Debug.LogError("StudioLinks.Wire: no target; use Clear to empty a slot.");
                return;
            }
            if (!TryFind(slot, out var serialized, out var property))
                return;
            if (slot.IsList)
            {
                RemoveNulls(property);
                if (IndexOf(property, target) >= 0)
                    return;
                var index = property.arraySize;
                property.arraySize = index + 1;
                property.GetArrayElementAtIndex(index).objectReferenceValue = target;
            }
            else
                property.objectReferenceValue = target;
            serialized.ApplyModifiedProperties();
            StudioEdits.Edited(slot.Effect.gameObject);
        }

        /// <summary>Removes <paramref name="target"/> from a slot: a list slot shrinks by that entry, a single slot clears if it held it.</summary>
        public static void Unwire(in Slot slot, GameObject target)
        {
            if (target == null || !TryFind(slot, out var serialized, out var property))
                return;
            if (slot.IsList)
            {
                var index = IndexOf(property, target);
                if (index < 0)
                    return;
                RemoveAt(property, index);
                RemoveNulls(property);
            }
            else
            {
                if (property.objectReferenceValue != target)
                    return;
                property.objectReferenceValue = null;
            }
            serialized.ApplyModifiedProperties();
            StudioEdits.Edited(slot.Effect.gameObject);
        }

        /// <summary>Empties a slot: a list slot to no entries, a single slot to null.</summary>
        public static void Clear(in Slot slot)
        {
            if (!TryFind(slot, out var serialized, out var property))
                return;
            if (slot.IsList)
                property.arraySize = 0;
            else
                property.objectReferenceValue = null;
            serialized.ApplyModifiedProperties();
            StudioEdits.Edited(slot.Effect.gameObject);
        }

        static bool TryFind(in Slot slot, out SerializedObject serialized, out SerializedProperty property)
        {
            serialized = null;
            property = null;
            if (slot.Effect == null)
                return false;
            serialized = new SerializedObject(slot.Effect);
            property = serialized.FindProperty(slot.PropertyPath);
            if (property == null)
            {
                Debug.LogError($"StudioLinks: property '{slot.PropertyPath}' not found on '{slot.Effect.GetType().Name}'.");
                return false;
            }
            if (property.isArray != slot.IsList)
            {
                Debug.LogError($"StudioLinks: property '{slot.PropertyPath}' on '{slot.Effect.GetType().Name}' is {(property.isArray ? "" : "not ")}an array; the slot was read as {(slot.IsList ? "a list" : "single")}.");
                return false;
            }
            return true;
        }

        static int IndexOf(SerializedProperty list, GameObject target)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == target)
                    return i;
            }
            return -1;
        }

        static void RemoveAt(SerializedProperty list, int index)
        {
            // Null the entry first: DeleteArrayElementAtIndex on a live reference only nulls it and keeps the element.
            list.GetArrayElementAtIndex(index).objectReferenceValue = null;
            list.DeleteArrayElementAtIndex(index);
        }

        static void RemoveNulls(SerializedProperty list)
        {
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    list.DeleteArrayElementAtIndex(i);
            }
        }
    }

    /// <summary>
    /// The Studio's transient link-mode state, shared by both panes so a wire can cross faces:
    /// the source plate is picked in one pane and the target may be clicked in the other.
    /// </summary>
    public sealed class StudioLinkState
    {
        /// <summary>Link mode on/off (the toolbar toggle). Off clears the source.</summary>
        public bool Active;

        /// <summary>The plate whose target clicks wire and unwire; stays armed until Escape or a click on empty space. Null when no source is armed.</summary>
        public GameObject Source;

        public void Exit()
        {
            Active = false;
            Source = null;
        }
    }
}
