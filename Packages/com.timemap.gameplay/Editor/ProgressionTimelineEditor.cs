using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Default inspector for the asset plus an "Open" button.</summary>
    [CustomEditor(typeof(ProgressionTimeline))]
    public sealed class ProgressionTimelineEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var open = new Button(() => ProgressionTimelineWindow.Open((ProgressionTimeline)target)) { text = "Открыть в Progression Timeline" };
            open.style.height = 24;
            open.style.marginBottom = 6;
            root.Add(open);
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }
    }
}
