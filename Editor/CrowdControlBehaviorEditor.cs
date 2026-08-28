#if UNITY_EDITOR
using CrowdControl.Client.WebSocket.Actions;
using CrowdControl.Client.WebSocket.Data;
using CrowdControl.Common;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CrowdControl.Client.Unity.Editor
{
    [CustomEditor(typeof(CrowdControlBehavior))]
    public class CrowdControlBehaviorEditor : UnityEditor.Editor
    {
        private static readonly Regex VALID_GAME_ID = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            CrowdControlBehavior behavior = (target as CrowdControlBehavior)!;

            DrawLogFileLocation(behavior);

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
                        behavior.Connect();
                }

                using (new EditorGUI.DisabledScope((!Application.isPlaying) || (!behavior.isActiveAndEnabled) || (!behavior.Connected)))
                {
                    if (GUILayout.Button("Ping Test"))
                        behavior.Ping();
                }

                using (new EditorGUI.DisabledScope((!Application.isPlaying) || (!behavior.isActiveAndEnabled)))
                {
                    if (GUILayout.Button("Clear Login Token"))
                        behavior.ClearToken();
                }

                using (new EditorGUI.DisabledScope((!Application.isPlaying) || (!behavior.isActiveAndEnabled)))
                {
                    if (GUILayout.Button("Launch Interact Link"))
                        behavior.LaunchInteractLink();
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
                        GenerateMenuJson(behavior, effectLoader);

                    DrawCustomEffectTools(behavior, effectLoader);
                }
            }
        }

        /// <summary>Shows where this game's Crowd Control log is written, and offers to open it.</summary>
        /// <remarks>
        /// The path is under <c>Application.persistentDataPath</c>, which nothing in the inspector otherwise spells
        /// out, and its file name is close enough to the Crowd Control desktop application's own log that developers
        /// have gone looking in <c>%AppData%</c> and found the wrong file. Showing the resolved absolute path here,
        /// with a button that reveals it, removes the guesswork.
        /// </remarks>
        private static void DrawLogFileLocation(CrowdControlBehavior behavior)
        {
            if (!behavior.LogToFile) return;

            string path = behavior.ResolvedLogFilePath;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "This game's Crowd Control log is written to:\n" + path + "\n\n" +
                "That is inside this game's own data folder. It is not the Crowd Control desktop app's log, which " +
                "lives in %AppData%\\CrowdControl\\logs.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy Log Path"))
                    EditorGUIUtility.systemCopyBuffer = path;

                using (new EditorGUI.DisabledScope(!File.Exists(path)))
                {
                    if (GUILayout.Button("Show Log File"))
                        EditorUtility.RevealInFinder(path);
                }
            }
        }

        /// <summary>Writes the effect menu for this scene out as a game pack menu JSON file.</summary>
        private static void GenerateMenuJson(CrowdControlBehavior behavior, UnityEffectLoader effectLoader)
        {
            ReloadEffects(effectLoader);

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
                JObject nextItem = item.ToJObject();
                effects_game[item.EffectID] = nextItem;
            }

            string path = EditorUtility.SaveFilePanel(
                "Save Menu JSON",
                "",
                "menu.json",
                "json");

            if (string.IsNullOrEmpty(path)) return; //the save panel was cancelled

            File.WriteAllText(path, result.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        /// <summary>
        /// Draws the custom effect upload tools.
        /// </summary>
        /// <remarks>
        /// These are development tools. Uploading rewrites the custom effect list the Crowd Control service hands out
        /// for this game pack, so it changes what every viewer of the game sees, not just this machine. The service
        /// only allows it with a developer token carrying the <c>custom-effects:write</c> scope, and only for game
        /// packs configured with <c>allowCustomEffects</c>. A released game should ship the generated menu JSON in its
        /// game pack instead of uploading effects at runtime.
        /// </remarks>
        private static void DrawCustomEffectTools(CrowdControlBehavior behavior, UnityEffectLoader effectLoader)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Custom Effects (Development Tool)", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Development tool. Uploading publishes these effects to the Crowd Control service for this game pack, " +
                "which changes what every viewer sees. It requires a developer login with the 'custom-effects:write' " +
                "scope and a game pack that allows custom effects.\n\n" +
                "Do not rely on this for a released game: ship the generated menu JSON in your game pack instead.",
                MessageType.Warning);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode and connect to upload custom effects.", MessageType.Info);
                return;
            }

            if (!behavior.Connected)
            {
                EditorGUILayout.HelpBox("Connect to Crowd Control to upload custom effects.", MessageType.Info);
                return;
            }

            if (!behavior.CanUploadCustomEffects)
            {
                EditorGUILayout.HelpBox(
                    "The current login does not have the 'custom-effects:write' scope, so custom effects cannot be " +
                    "uploaded. Contact the Crowd Control team if you need a developer token for this game pack.",
                    MessageType.Info);
                return;
            }

            int customEffectCount = CountCustomEffects(effectLoader);
            EditorGUILayout.LabelField("Custom effects in scene", customEffectCount.ToString());

            using (new EditorGUI.DisabledScope(customEffectCount == 0))
            {
                if (GUILayout.Button("Upload Custom Effects (Merge)"))
                {
                    ReloadEffects(effectLoader);
                    behavior.UpdateCustomEffects();
                }

                if (GUILayout.Button("Upload Custom Effects (Replace All)")
                    && EditorUtility.DisplayDialog(
                        "Replace All Custom Effects",
                        "This replaces every custom effect registered for game pack '" + behavior.GameID + "' with the " +
                        "effects in this scene. Custom effects uploaded from anywhere else will be discarded.\n\nContinue?",
                        "Replace All",
                        "Cancel"))
                {
                    ReloadEffects(effectLoader);
                    behavior.UploadCustomEffects(CustomEffects.OperationMode.ReplaceAll).Forget();
                }
            }

            if (GUILayout.Button("Delete All Custom Effects")
                && EditorUtility.DisplayDialog(
                    "Delete All Custom Effects",
                    "This removes every custom effect registered for game pack '" + behavior.GameID + "', including " +
                    "effects uploaded from another machine.\n\nContinue?",
                    "Delete All",
                    "Cancel"))
                behavior.DeleteAllCustomEffects().Forget();
        }

        /// <summary>Rescans the scene so the effect registry matches what is currently there.</summary>
        private static void ReloadEffects(UnityEffectLoader effectLoader)
        {
            ((IEffectLoader)effectLoader).Unload();
            ((IEffectLoader)effectLoader).Load();
        }

        /// <summary>Counts the effects in the scene that are flagged as custom effects.</summary>
        private static int CountCustomEffects(UnityEffectLoader effectLoader)
        {
            int count = 0;
            foreach (IEffect effect in effectLoader.Effects.Values)
                if (effect.IsCustom) count++;
            return count;
        }
    }
}
#endif