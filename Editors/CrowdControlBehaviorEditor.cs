#if UNITY_EDITOR
using CrowdControl.Client.WebSocket.Actions;
using Newtonsoft.Json.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CrowdControl.Client.Unity.Editors
{
    [CustomEditor(typeof(CrowdControlBehavior))]
    public class CrowdControlBehaviorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            CrowdControlBehavior? behavior = target as CrowdControlBehavior;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope((!Application.isPlaying) || (behavior is null) || (!behavior.isActiveAndEnabled)))
            {
                if (GUILayout.Button("Connect"))
                {
                    behavior?.Connect();
                }
            }

            using (new EditorGUI.DisabledScope((!Application.isPlaying) || (behavior is null) || (!behavior.isActiveAndEnabled) || (!behavior.Connected)))
            {
                if (GUILayout.Button("Ping Test"))
                {
                    behavior?.Ping();
                }
            }

            EditorGUILayout.Space();

            UnityEffectLoader effectLoader = FindFirstObjectByType<UnityEffectLoader>();

            if (effectLoader != null)
            {
                if (GUILayout.Button("Generate Menu JSON"))
                {
                    ((IEffectLoader)effectLoader).Unload();
                    ((IEffectLoader)effectLoader).Load();

                    JObject result = new();

                    JObject meta = (JObject)(result["meta"] = new JObject());
                    meta["platform"] = "PC";
                    meta["name"] = behavior.DisplayName;
                    meta["connector"] = JArray.FromObject(new[] { "External" });
                    meta["guide"] = "https://crowdcontrol.live/guides/" + behavior.GameID;

                    JObject effects = (JObject)(result["effects"] = new JObject());
                    JObject effects_game = (JObject)(effects["game"] = new JObject());

                    foreach (UnityEffectBase item in effectLoader.Effects.Values)
                    {
                        JObject nextItem = new JObject
                        {
                            ["id"] = item.EffectID,
                            ["name"] = item.name
                        };
                        effects_game[item.EffectID] = nextItem;
                    }

                    string path = EditorUtility.SaveFilePanel(
                        "Save Menu JSON",
                        "",
                        "menu.json",
                        "json");

                    File.WriteAllText(path, result.ToString(Newtonsoft.Json.Formatting.Indented));
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No UnityEffectLoader was found. Please add one to the scene.", MessageType.Error);
            }
        }
    }
}
#endif