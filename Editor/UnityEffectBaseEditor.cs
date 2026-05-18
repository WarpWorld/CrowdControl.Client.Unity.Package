#if UNITY_EDITOR
using System.Collections.Generic;
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
        private readonly Dictionary<string, string> testOptionValues = new();
        private readonly Dictionary<string, Color> testColorValues = new();

        private bool isTimed;
        private bool hasQuantity;

        /// <summary>Custom inspector GUI that adds buttons to test each effect ID during play mode.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty defaultDurationProperty = serializedObject.FindProperty(nameof(UnityEffectBase.DefaultDuration));
            SerializedProperty maxQuantityProperty = serializedObject.FindProperty(nameof(UnityEffectBase.MaxQuantity));

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((UnityEffectBase)target), typeof(UnityEffectBase), false);

            DrawPropertiesExcluding(serializedObject, "m_Script", nameof(UnityEffectBase.DefaultDuration), nameof(UnityEffectBase.MaxQuantity));

            isTimed = EditorGUILayout.Toggle("Timed Effect", isTimed);
            if (isTimed)
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
                defaultDurationProperty.intValue = 0;

            hasQuantity = EditorGUILayout.Toggle("Quantity Effect", hasQuantity);
            if (hasQuantity)
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
                maxQuantityProperty.longValue = 1;

            serializedObject.ApplyModifiedProperties();

            if (!Application.isPlaying)
                return;

            UnityEffectBase effect = (UnityEffectBase)target;
            string effectID = effect.EffectID;
            uint maxQuantity = effect.MaxQuantity;
            int maxTestQuantity = maxQuantity > int.MaxValue ? int.MaxValue : (int)maxQuantity;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);

            if (effect.HasQuantity)
                testQuantity = EditorGUILayout.IntSlider("Quantity", Mathf.Clamp(testQuantity, 1, maxTestQuantity), 1, maxTestQuantity);
            else
                testQuantity = 1;

            DrawTestParameters(effect.Parameters);

            if (GUILayout.Button("Test " + effectID))
            {
                uint quantity = (uint)testQuantity;

                EffectRequest request = new(effectID);
                request.Quantity = quantity;

                ParameterResults parameters = BuildTestParameters(effect.Parameters);
                if (parameters.Count > 0)
                    request.Parameters = parameters;

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

        private void DrawTestParameters(ParameterDef[]? parameters)
        {
            if ((parameters == null) || (parameters.Length == 0))
                return;

            foreach (ParameterDef parameter in parameters)
            {
                switch (parameter.Type)
                {
                    case ParameterBase.ParameterType.Options:
                        DrawOptionTestParameter(parameter);
                        break;
                    case ParameterBase.ParameterType.HexColor:
                        DrawColorTestParameter(parameter);
                        break;
                }
            }
        }

        private void DrawOptionTestParameter(ParameterDef parameter)
        {
            if ((parameter.Options == null) || (parameter.Options.Count == 0))
                return;

            int selectedIndex = GetSelectedOptionIndex(parameter);
            string[] displayedOptions = new string[parameter.Options.Count];
            for (int i = 0; i < parameter.Options.Count; i++)
                displayedOptions[i] = parameter.Options[i].Name;

            int updatedIndex = EditorGUILayout.Popup(parameter.Name, selectedIndex, displayedOptions);
            testOptionValues[parameter.ID] = parameter.Options[updatedIndex].ID;
        }

        private void DrawColorTestParameter(ParameterDef parameter)
        {
            if (!testColorValues.TryGetValue(parameter.ID, out Color value))
                value = Color.white;

            testColorValues[parameter.ID] = EditorGUILayout.ColorField(parameter.Name, value);
        }

        private int GetSelectedOptionIndex(ParameterDef parameter)
        {
            if ((parameter.Options == null) || (parameter.Options.Count == 0))
                return 0;

            if (testOptionValues.TryGetValue(parameter.ID, out string selectedID))
            {
                for (int i = 0; i < parameter.Options.Count; i++)
                {
                    if (parameter.Options[i].ID == selectedID)
                        return i;
                }
            }

            testOptionValues[parameter.ID] = parameter.Options[0].ID;
            return 0;
        }

        private ParameterResults BuildTestParameters(ParameterDef[]? parameters)
        {
            if ((parameters == null) || (parameters.Length == 0))
                return ParameterResults.Empty;

            List<IParameterValue> values = new();
            foreach (ParameterDef parameter in parameters)
            {
                switch (parameter.Type)
                {
                    case ParameterBase.ParameterType.Options:
                    {
                        if ((parameter.Options == null) || (parameter.Options.Count == 0))
                            continue;

                        int selectedIndex = GetSelectedOptionIndex(parameter);
                        values.Add(new ParameterValue<string>(parameter.Name, parameter.ID, parameter.Options[selectedIndex].ID));
                        break;
                    }
                    case ParameterBase.ParameterType.HexColor:
                    {
                        if (!testColorValues.TryGetValue(parameter.ID, out Color color))
                            color = Color.white;

                        values.Add(new ParameterColor(parameter.Name, parameter.ID, "#" + ColorUtility.ToHtmlStringRGBA(color)));
                        break;
                    }
                }
            }

            return (values.Count > 0) ? new ParameterResults(values) : ParameterResults.Empty;
        }

        /*private IEnumerator StopEffectCoroutine(UnityEffectBase effect, EffectRequest request)
        {
            yield return new EditorWaitForSeconds((float)request.Duration.TotalSeconds);
            effect.StopEffect(request);
        }*/
    }
}
#endif