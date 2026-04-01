#if UNITY_EDITOR
using CrowdControl.Common;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CrowdControl.Client.Unity.Editors
{
    /// <summary>Custom editor for UnityEffectBase that adds buttons to test each effect ID during play mode.</summary>
    [CustomEditor(typeof(UnityEffectBase), true)]
    public class UnityEffectBaseEditor : Editor
    {
        private SynchronizationContext? m_context;

        void OnEnable() => m_context = SynchronizationContext.Current;

        /// <summary>Custom inspector GUI that adds buttons to test each effect ID during play mode.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (Application.isPlaying)
            {
                UnityEffectBase effect = (UnityEffectBase)target;
                string effectID = effect.EffectID;
                if (GUILayout.Button("Test " + effectID))
                {
                    EffectRequest request = new(effectID);
                    if (effect.IsTimed)
                    {
                        Log.Debug("Starting timed effect: " + effectID);
                        effect.StartEffect(request);
                        StopEffectAfterDelay(effect, request).Forget();
                    }
                    else
                    {
                        Log.Debug("Starting instant effect: " + effectID);
                        effect.StartEffect(request);
                    }
                }
            }
        }

        private async Task StopEffectAfterDelay(UnityEffectBase effect, EffectRequest request)
        {
            await Task.Delay((TimeSpan)request.Duration);
            m_context?.Post(_ => effect.StopEffect(request), null);
        }
    }
}
#endif