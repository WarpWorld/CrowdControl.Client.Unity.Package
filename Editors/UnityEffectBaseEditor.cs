#if UNITY_EDITOR
using CrowdControl.Common;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

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

                    // Schedule stop safely without blocking GUI or relying on SynchronizationContext
                    Task.Run(async () =>
                    {
                        await Task.Delay((TimeSpan)request.Duration);
                        EditorApplication.delayCall += () => effect.StopEffect(request);
                    });
                }
                else
                {
                    Log.Debug("Starting instant effect: " + effectID);
                    effect.StartEffect(request);
                }
            }
        }
    }
}
#endif