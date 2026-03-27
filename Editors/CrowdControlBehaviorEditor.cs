#if UNITY_EDITOR
using CrowdControl.Client.WebSocket.Actions;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CrowdControl.Client.Unity.Editors
{
    [CustomEditor(typeof(CrowdControlBehavior))]
    public class CrowdControlBehaviorEditor : Editor
    {
        private static readonly Regex VALID_GAME_ID = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            CrowdControlBehavior behavior = (target as CrowdControlBehavior)!;

            EditorGUILayout.Space();

            if (string.IsNullOrWhiteSpace(behavior.GameID))
                EditorGUILayout.HelpBox("Game ID is required. If you do not have a Game ID, please contact the Crowd Control team to have one created for you.", MessageType.Error);
            else if (!VALID_GAME_ID.IsMatch(behavior.GameID))
                EditorGUILayout.HelpBox("Game ID is invalid. It must start with a letter or underscore and can only contain letters, numbers, and underscores.", MessageType.Error);
            else
            {
                using (new EditorGUI.DisabledScope((!Application.isPlaying) || (!behavior.isActiveAndEnabled)))
                {
                    if (GUILayout.Button("Connect"))
                    {
                        behavior.Connect();
                    }
                }

                using (new EditorGUI.DisabledScope((!Application.isPlaying) || (!behavior.isActiveAndEnabled) || (!behavior.Connected)))
                {
                    if (GUILayout.Button("Ping Test"))
                    {
                        behavior.Ping();
                    }
                }

                EditorGUILayout.Space();

                UnityEffectLoader effectLoader = FindFirstObjectByType<UnityEffectLoader>();

                if (string.IsNullOrWhiteSpace(behavior.DisplayName))
                    EditorGUILayout.HelpBox("Display Name is required. Please set it to the name of your game as it appears on the Crowd Control website.", MessageType.Error);
                else if (effectLoader == null)
                    EditorGUILayout.HelpBox("No UnityEffectLoader was found. Please add one to the scene.", MessageType.Error);
                else
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
            }
        }
    }
}
#endif