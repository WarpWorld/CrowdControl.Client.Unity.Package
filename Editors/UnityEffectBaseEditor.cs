#if UNITY_EDITOR
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
                            CrowdControlBehavior? crowdControl = FindFirstObjectByType<CrowdControlBehavior>();
                            crowdControl?.Scheduler.ProcessRequest(new(effectID));
                        }
                        else
                            effect.StartEffect(new(effectID));
                    }
        }
    }
}
#endif