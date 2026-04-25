#if UNITY_EDITOR
using CrowdControl.Common;
using UnityEditor;
using UnityEngine;

namespace CrowdControl.Client.Unity.Editor
{
    [CustomPropertyDrawer(typeof(ParameterDef), true)]
    public class ParameterDefDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty id = property.FindPropertyRelative("ID");
            SerializedProperty name = property.FindPropertyRelative("Name");
            SerializedProperty type = property.FindPropertyRelative("Type");
            SerializedProperty options = property.FindPropertyRelative("Options");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect lineRect = new(position.x, position.y, position.width, lineHeight);

            EditorGUI.BeginProperty(position, label, property);
            property.isExpanded = EditorGUI.Foldout(lineRect, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;

                lineRect.y += lineHeight + spacing;
                EditorGUI.PropertyField(lineRect, id);

                lineRect.y += lineHeight + spacing;
                EditorGUI.PropertyField(lineRect, name);

                lineRect.y += lineHeight + spacing;
                EditorGUI.PropertyField(lineRect, type);

                if ((ParameterBase.ParameterType)type.enumValueIndex == ParameterBase.ParameterType.Options)
                {
                    lineRect.y += lineHeight + spacing;
                    lineRect.height = EditorGUI.GetPropertyHeight(options, true);
                    EditorGUI.PropertyField(lineRect, options, true);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty id = property.FindPropertyRelative("ID");
            SerializedProperty name = property.FindPropertyRelative("Name");
            SerializedProperty type = property.FindPropertyRelative("Type");
            SerializedProperty options = property.FindPropertyRelative("Options");

            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            height += spacing + EditorGUI.GetPropertyHeight(id);
            height += spacing + EditorGUI.GetPropertyHeight(name);
            height += spacing + EditorGUI.GetPropertyHeight(type);

            if ((ParameterBase.ParameterType)type.enumValueIndex == ParameterBase.ParameterType.Options)
                height += spacing + EditorGUI.GetPropertyHeight(options, true);

            return height;
        }
    }
}
#endif
