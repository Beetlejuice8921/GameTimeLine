using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Popup with timeline items of the allowed type for <see cref="ItemReference"/> fields.</summary>
    [CustomPropertyDrawer(typeof(ItemReferenceAttribute))]
    public sealed class ItemReferenceDrawer : PropertyDrawer
    {
        const string None = "";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var idProperty = property.FindPropertyRelative("_id");
            var timeline = property.serializedObject.targetObject as ProgressionTimeline;
            var itemType = ((ItemReferenceAttribute)attribute).ItemType;

            var items = timeline == null
                ? new List<TimelineItem>()
                : timeline.Tracks.Where(t => t != null).SelectMany(t => t.Items)
                    .Where(i => i != null && itemType.IsInstanceOfType(i))
                    .OrderBy(i => i.Start)
                    .ToList();

            var names = items.ToDictionary(i => i.Id, i => $"{i.Name}  ({TimeMath.Format(i.Start, timeline.Axis.Units)})");
            var choices = new List<string> { None };
            choices.AddRange(items.Select(i => i.Id));
            string current = idProperty.stringValue ?? None;
            if (!choices.Contains(current)) choices.Add(current);

            string Format(string id) => string.IsNullOrEmpty(id) ? "—" : names.TryGetValue(id, out var n) ? n : "(удалён)";

            var field = new PopupField<string>(preferredLabel, choices, current, Format, Format);
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            field.RegisterValueChangedCallback(e =>
            {
                idProperty.stringValue = e.newValue;
                idProperty.serializedObject.ApplyModifiedProperties();
            });
            field.TrackPropertyValue(idProperty, p =>
            {
                if (choices.Contains(p.stringValue)) field.SetValueWithoutNotify(p.stringValue);
            });
            return field;
        }
    }
}
