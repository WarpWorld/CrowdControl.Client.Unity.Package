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
        /// <summary>Custom inspector GUI that adds buttons to test each effect ID during play mode.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            UnityEffectBase effect = (UnityEffectBase)target;

            if (Application.isPlaying)
                foreach (string effectID in effect.EffectAttribute.IDs)
                    if (GUILayout.Button("Test " + effectID))
                    {
                        if (effect.IsTimed)
                        {
                            Log.Debug("Starting timed effect: " + effectID);
                            SynchronizationContext context = SynchronizationContext.Current;
                            EffectRequest request = new(effectID);
                            effect.StartEffect(request);
                            StopEffectAfterDelay(effect, request, context).Forget();
                        }
                        else
                        {
                            Log.Debug("Starting instant effect: " + effectID);
                            effect.StartEffect(new(effectID));
                        }
                    }
        }

        private async Task StopEffectAfterDelay(UnityEffectBase effect, EffectRequest request, SynchronizationContext context)
        {
            await Task.Delay((TimeSpan)request.Duration);
            context.Post(_ => effect.StopEffect(request), null);
        }
    }
}
#endif