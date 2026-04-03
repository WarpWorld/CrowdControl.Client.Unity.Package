#if UNITY_EDITOR
using CrowdControl.Client.Unity.Editor.Shims;
using CrowdControl.Client.WebSocket.Metadata;
using UnityEditor;
using UnityEngine;

namespace CrowdControl.Client.Unity.Editor
{
    /// <summary>Custom editor for UnityMetadataBase that displays the runtime Value in the inspector.</summary>
    [CustomEditor(typeof(UnityMetadataBase), true)]
    public class UnityMetadataBaseEditor : UnityEditor.Editor
    {
        /// <summary>Custom inspector GUI that adds a read-only display of the Value during play mode.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // Only show the runtime Value when the game is running.
            if (!Application.isPlaying)
                return;

            if (target is not UnityMetadataBase metadata)
                return;

            using (new EditorGUI.DisabledScope(true))
            {
                string valueText;
                try
                {
                    var value = ((IMetadata)metadata).Value;
                    valueText = value != null ? value.ToString() ?? "null" : "null";
                }
                catch (System.Exception ex)
                {
                    valueText = $"Error: {ex.Message}";
                }
                EditorGUILayout.TextField("Value", valueText);
            }
        }
    }
}
#endif