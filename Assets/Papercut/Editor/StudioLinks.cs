using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// Wiring between plates and what their effects act on, for the Studio's link mode. Target slots are
    /// discovered generically — every serialized GameObject-reference field on every <see cref="PlateEffect"/>
    /// under an element — so a future effect kind gets Studio wiring with no editor change.
    /// </summary>
    public static class StudioLinks
    {
        /// <summary>One wireable target field on one effect component.</summary>
        public readonly struct Slot
        {
            public readonly PlateEffect Effect;
            public readonly string PropertyPath;
            public readonly string DisplayName;
            public readonly GameObject Target;

            public Slot(PlateEffect effect, string propertyPath, string displayName, GameObject target)
            {
                Effect = effect;
                PropertyPath = propertyPath;
                DisplayName = displayName;
                Target = target;
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
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.type != GameObjectReferenceType)
                        continue;
                    slots.Add(new Slot(
                        effect,
                        property.propertyPath,
                        $"{ObjectNames.NicifyVariableName(effect.GetType().Name)} → {property.displayName}",
                        property.objectReferenceValue as GameObject));
                }
            }
            return slots;
        }

        public static bool HasSlots(GameObject element) => GetSlots(element).Count > 0;

        /// <summary>
        /// Points a slot at <paramref name="target"/> (null clears it). Undoable — goes through
        /// SerializedObject, the same path as an Inspector edit.
        /// </summary>
        public static void Wire(in Slot slot, GameObject target)
        {
            if (slot.Effect == null)
                return;
            var serialized = new SerializedObject(slot.Effect);
            var property = serialized.FindProperty(slot.PropertyPath);
            if (property == null)
            {
                Debug.LogError($"StudioLinks.Wire: property '{slot.PropertyPath}' not found on '{slot.Effect.GetType().Name}'.");
                return;
            }
            property.objectReferenceValue = target;
            serialized.ApplyModifiedProperties();
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

        /// <summary>The plate whose next slot click wires; null when no source is armed.</summary>
        public GameObject Source;

        public void Exit()
        {
            Active = false;
            Source = null;
        }
    }
}
