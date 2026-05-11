#if UNITY_EDITOR
using CrowdControl.Common;
using UnityEditor;
using UnityEngine;

namespace CrowdControl.Client.Unity.Editor
{
    /// <summary>Custom editor for UnityEffectBase that adds buttons to test each effect ID during play mode.</summary>
    [CustomEditor(typeof(UnityEffectBase), true)]
    public class UnityEffectBaseEditor : UnityEditor.Editor
    {
        private int testQuantity = 1;

        /// <summary>Custom inspector GUI that adds buttons to test each effect ID during play mode.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty defaultDurationProperty = serializedObject.FindProperty(nameof(UnityEffectBase.DefaultDuration));
            SerializedProperty maxQuantityProperty = serializedObject.FindProperty(nameof(UnityEffectBase.MaxQuantity));

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((UnityEffectBase)target), typeof(UnityEffectBase), false);

            DrawPropertiesExcluding(serializedObject, "m_Script", nameof(UnityEffectBase.DefaultDuration), nameof(UnityEffectBase.MaxQuantity));

            bool isTimed = defaultDurationProperty.intValue > 0;
            bool showDefaultDuration = EditorGUILayout.Toggle("Timed Effect", isTimed);

            if (showDefaultDuration)
            {
                if (defaultDurationProperty.intValue < 1)
                    defaultDurationProperty.intValue = 1;

                defaultDurationProperty.intValue = EditorGUILayout.IntSlider(
                    new GUIContent(defaultDurationProperty.displayName, defaultDurationProperty.tooltip),
                    defaultDurationProperty.intValue,
                    1,
                    600);
            }
            else
            {
                defaultDurationProperty.intValue = 0;
            }

            bool hasQuantity = maxQuantityProperty.longValue > 1;
            bool showMaxQuantity = EditorGUILayout.Toggle("Quantity Effect", hasQuantity);

            if (showMaxQuantity)
            {
                if (maxQuantityProperty.longValue < 1)
                    maxQuantityProperty.longValue = 1;

                maxQuantityProperty.longValue = EditorGUILayout.IntSlider(
                    new GUIContent(maxQuantityProperty.displayName, maxQuantityProperty.tooltip),
                    (int)maxQuantityProperty.longValue,
                    1,
                    10_000);
            }
            else
            {
                maxQuantityProperty.longValue = 1;
            }

            serializedObject.ApplyModifiedProperties();

            if (!Application.isPlaying)
                return;

            UnityEffectBase effect = (UnityEffectBase)target;
            string effectID = effect.EffectID;
            uint maxQuantity = effect.MaxQuantity;
            int maxTestQuantity = maxQuantity > int.MaxValue ? int.MaxValue : (int)maxQuantity;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);

            if (maxQuantity <= 1)
                testQuantity = 1;
            else
                testQuantity = EditorGUILayout.IntSlider("Quantity", Mathf.Clamp(testQuantity, 1, maxTestQuantity), 1, maxTestQuantity);

            if (GUILayout.Button("Test " + effectID))
            {
                uint quantity = (uint)testQuantity;

                EffectRequest request = new(effectID);
                request.Quantity = quantity;

                if (effect.IsTimed)
                {
                    Log.Debug("Testing timed effects in the editor is not currently supported.");
                    //Log.Debug("Starting timed effect: " + effectID);
                    //effect.StartEffect(request);
                    //EditorCoroutineUtility.StartCoroutineOwnerless(StopEffectCoroutine(effect, request));
                }
                else
                {
                    Log.Debug("Starting instant effect: " + effectID);
                    effect.StartEffect(request);
                }
            }
        }

        /*private IEnumerator StopEffectCoroutine(UnityEffectBase effect, EffectRequest request)
        {
            yield return new EditorWaitForSeconds((float)request.Duration.TotalSeconds);
            effect.StopEffect(request);
        }*/
    }
}
#endif