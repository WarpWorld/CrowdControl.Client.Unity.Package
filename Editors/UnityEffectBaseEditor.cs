#if UNITY_EDITOR
using CrowdControl.Common;
using System.Collections;
using UnityEditor;
using UnityEngine;
using Unity.EditorCoroutines.Editor;

namespace CrowdControl.Client.Unity.Editors
{
    /// <summary>Custom editor for UnityEffectBase that adds buttons to test each effect ID during play mode.</summary>
    [CustomEditor(typeof(UnityEffectBase), true)]
    public class UnityEffectBaseEditor : Editor
    {
        /// <summary>Custom inspector GUI that adds buttons to test each effect ID during play mode.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (!Application.isPlaying)
                return;

            UnityEffectBase effect = (UnityEffectBase)target;
            string effectID = effect.EffectID;

            if (GUILayout.Button("Test " + effectID))
            {
                EffectRequest request = new(effectID);

                if (effect.IsTimed)
                {
                    Log.Debug("Starting timed effect: " + effectID);
                    effect.StartEffect(request);
                    EditorCoroutineUtility.StartCoroutineOwnerless(StopEffectCoroutine(effect, request));
                }
                else
                {
                    Log.Debug("Starting instant effect: " + effectID);
                    effect.StartEffect(request);
                }
            }
        }

        private IEnumerator StopEffectCoroutine(UnityEffectBase effect, EffectRequest request)
        {
            yield return new EditorWaitForSeconds((float)request.Duration.TotalSeconds);
            effect.StopEffect(request);
        }
    }
}
#endif