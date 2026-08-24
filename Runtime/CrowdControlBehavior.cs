using CrowdControl.Client.WebSocket;
using CrowdControl.Client.WebSocket.Actions;
using CrowdControl.Client.WebSocket.Data;
using CrowdControl.Client.WebSocket.Metadata;
using CrowdControl.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace CrowdControl.Client.Unity
{
    /// <summary>Unity MonoBehaviour that wires up and manages the Crowd Control client lifecycle for a scene.</summary>
    /// <remarks>Requires a <see cref="UnityGameStateManager"/> to provide game state and a <see cref="UnityEffectLoader"/> to expose available effects.</remarks>
    public class CrowdControlBehavior : MonoBehaviour, IDisposable
    {
        /// <summary>The game identifier used when connecting to the Crowd Control service.</summary>
        [SerializeField]
        [Tooltip("The game identifier used when connecting to the Crowd Control service.")]
        public string GameID;

        /// <summary>The display name used when connecting to the Crowd Control service.</summary>
        [SerializeField]
        [Tooltip("The display name used when connecting to the Crowd Control service.")]
        public string DisplayName;

        /// <summary>The application identifier used for authentication with the Crowd Control service.</summary>
        [SerializeField]
        [Tooltip("The application identifier used for authentication with the Crowd Control service.")]
        public string ApplicationID;

        /// <summary>Component that provides the current <see cref="WebSocket.GameState"/> to Crowd Control.</summary>
        [SerializeField]
        [Tooltip("Component responsible for providing the current game state to Crowd Control.")]
        public UnityGameStateManager? GameStateManager;

        /// <summary>Component responsible for finding and registering <see cref="UnityEffectBase"/> instances.</summary>
        [SerializeField]
        [Tooltip("Component responsible for finding and registering effect instances.")]
        public UnityEffectLoader? EffectLoader;

        /// <summary>Component responsible for finding and registering <see cref="UnityMetadataBase"/> instances.</summary>
        [SerializeField]
        [Tooltip("Component responsible for finding and registering metadata instances.")]
        public UnityMetadataLoader? MetadataLoader;

        /*/// <summary>
        /// Types of effects that this client will accept from the Crowd Control service.
        /// Effects of other types will be silently discarded.
        /// </summary>
        /// <remarks>Do not change this value without consulting Crowd Control support.</remarks>
        [SerializeField]
        [Tooltip("Types of effects that this client will accept from the Crowd Control service. Effects of other types will be silently discarded. Do not change this value without consulting Crowd Control support.")]
        public EffectRequest.EffectType allowedTypes = EffectRequest.EffectType.Game;*/

        /// <summary>Whether to automatically connect to Crowd Control on start.</summary>
        [SerializeField]
        [Tooltip("Whether to automatically connect to Crowd Control on start.")]
        public bool AutoConnect = false;

        /// <summary>Whether to automatically reconnect to Crowd Control when the connection is lost while a session is active.</summary>
        [SerializeField]
        [Tooltip("Whether to automatically reconnect to Crowd Control when the connection is lost while a session is active.")]
        public bool AutoReconnect = true;

        /// <summary>Whether to automatically upload the custom effects defined in the EffectLoader to the service when a session becomes ready.</summary>
        /// <remarks>
        /// <b>Development tool. Do not ship a build with this enabled.</b> Uploading effects rewrites the game pack's
        /// custom effect list on the Crowd Control service for every viewer, and it only works at all with a developer
        /// token carrying the <c>custom-effects:write</c> scope. It is ignored outside the Unity Editor, and a released
        /// game should have its effects published in its game pack menu instead.
        /// </remarks>
        [SerializeField]
        [Tooltip("DEVELOPMENT TOOL. Automatically uploads the custom effects defined in the EffectLoader to the service when a session becomes ready. " +
                 "Requires a developer token with the 'custom-effects:write' scope, and is ignored outside the Unity Editor. " +
                 "Released games should publish their effects in their game pack menu instead.")]
        public bool AutoAddCustomEffects = false;

        /// <summary>Whether to log every raw inbound and outbound WebSocket frame to the Unity console.</summary>
        /// <remarks>
        /// Diagnostic only. This is verbose: every frame becomes a console line, including keepalive ping/pong
        /// traffic. Connection open, close and error events are logged regardless of this setting.
        /// </remarks>
        [SerializeField]
        [Tooltip("Log every raw WebSocket frame sent and received. Verbose - diagnostic use only. " +
                 "Connection open/close/error events are always logged regardless of this setting.")]
        public bool LogSocketTraffic = false;

        /// <summary>Whether to write the Crowd Control log to a file in addition to the Unity console.</summary>
        /// <remarks>
        /// Useful for capturing a session to send to support: the file has no Unity stack traces and survives a hang
        /// or a force-kill, because every line is flushed as it is written.
        /// </remarks>
        [SerializeField]
        [Tooltip("Also write the Crowd Control log to a file. Useful for capturing a session to send to support.")]
        public bool LogToFile = false;

        /// <summary>Where to write the log file when <see cref="LogToFile"/> is enabled.</summary>
        /// <remarks>
        /// Leave blank for <c>&lt;persistentDataPath&gt;/CrowdControl/crowdcontrol.log</c>. A relative path is taken
        /// relative to <see cref="Application.persistentDataPath"/>; an absolute path is used as given. The previous
        /// run's file is kept alongside it with a <c>.prev</c> extension.
        /// </remarks>
        [SerializeField]
        [Tooltip("Log file path. Blank = <persistentDataPath>/CrowdControl/crowdcontrol.log. " +
                 "Relative paths are resolved against persistentDataPath. The previous run is kept as .prev.")]
        public string LogFilePath = "";

        /// <summary>No longer used. Ping responses are always reported asynchronously.</summary>
        /// <remarks>
        /// This previously blocked the calling thread until a ping was answered. Doing that on the Unity main thread
        /// deadlocks: the ping completes on a background thread and the result is reported back through the main
        /// thread, which cannot run while it is blocked waiting. The field is retained so existing prefabs and scenes
        /// keep deserializing cleanly.
        /// </remarks>
        [SerializeField]
        [Tooltip("No longer used. Ping responses are always reported asynchronously.")]
        public bool WaitForPingResponse = true;

        /// <summary>Whether to persist the JWT token for reconnecting between executions.</summary>
        [SerializeField]
        [Tooltip("Whether to persist the JWT token for reconnecting between executions.")]
        public bool PersistLoginToken = true;

        /// <summary>Whether to preserve the manager when switching between scenes.</summary>
        [SerializeField]
        [Tooltip("Whether to preserve the manager when switching between scenes.")]
        public bool PreserveBetweenScenes = false;

        /// <summary>
        /// Backing field for the JWT token used for authentication with the Crowd Control service.
        /// This is set when a new token is received and is used for reconnecting if the connection is lost.
        /// </summary>
        private string? m_jwt;

        /// <summary>Gets a value indicating whether there is a valid JWT token available for authentication with the Crowd Control service.</summary>
        public bool HasValidToken => CrowdControl?.IsTokenValid() ?? false;

        /// <summary>Gets a value indicating whether the Crowd Control client is currently connected.</summary>
        /// <remarks>True if the client is created and connected; false otherwise.</remarks>
        public bool Connected => CrowdControl?.Connected ?? false;

        /// <summary>Gets the <see cref="Scheduler"/> instance used by the Crowd Control client for scheduling effect execution.</summary>
        /// <remarks>Throws an exception if the client is not initialized.</remarks>
        public Scheduler Scheduler => CrowdControl?.Scheduler ?? throw new InvalidOperationException("Crowd Control client is not initialized.");

        private SynchronizationContext? m_synchronizationContext;

        private UnityMainThreadTaskScheduler? m_taskScheduler;

        /// <summary>Gets the underlying Crowd Control client instance after initialization.</summary>
        public WebSocket.CrowdControl? CrowdControl { get; private set; }

        /// <summary>Finalizer to ensure resources are released if <see cref="Dispose()"/> wasn't called.</summary>
        ~CrowdControlBehavior() => Dispose(false);

        /// <summary>Releases all resources used by this component and disconnects from Crowd Control.</summary>
        public void Dispose() => Dispose(true);

        /// <summary>Gets a value indicating whether this component has been disposed.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Core dispose pattern implementation.</summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>, false when from the finalizer.</param>
        protected void Dispose(bool disposing)
        {
            if (IsDisposed) return;
            IsDisposed = true;

            Stop();
            UnhookLogging();
            if (disposing) GC.SuppressFinalize(this);
        }

        /// <summary>Whether this instance currently holds a subscription to the Crowd Control log.</summary>
        private bool m_loggingHooked;

        /// <summary>The number of instances currently subscribed to the Crowd Control log.</summary>
        private static int s_loggingSubscribers;

        /// <summary>Writes a Crowd Control log message to the Unity console.</summary>
        /// <param name="message">The message to write.</param>
        /// <param name="level">The severity of the message.</param>
        private static void OnLogMessage(string message, LogLevel level)
        {
            //queued and written on a background thread; safe to call from the socket threads
            CrowdControlLogFile.Write($"[{level}] {message}");

            switch (level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                case LogLevel.Exception:
                    Debug.LogError(message);
                    break;
                case LogLevel.Message:
                    Debug.Log(message);
                    break;
                case LogLevel.Debug:
                    Debug.Log($"[Debug] {message}");
                    break;
                case LogLevel.Effect:
                    Debug.Log($"[Effect] {message}");
                    break;
            }
        }

        /// <summary>Routes the Crowd Control log to the Unity console for as long as this instance lives.</summary>
        /// <remarks>
        /// <see cref="Log.OnMessage"/> is a static event, so it outlives both this component and, when domain reloading
        /// is disabled, the play session itself. The subscription is therefore reference-counted and uses a method
        /// rather than a lambda, so that repeated Awake calls cannot stack up duplicate handlers.
        /// </remarks>
        private void HookLogging()
        {
            if (m_loggingHooked) return;
            m_loggingHooked = true;

            Log.FileOutput = false;
            Log.ConsoleOutput = false;

            if (Interlocked.Increment(ref s_loggingSubscribers) != 1) return;

            if (LogToFile)
            {
                string path = CrowdControlLogFile.ResolvePath(LogFilePath);
                if (CrowdControlLogFile.Open(path))
                    Debug.Log($"Crowd Control log file: {path}");
            }

            Debug.Log("Rerouting Crowd Control logs to Unity console...");
            Log.OnMessage += OnLogMessage;
        }

        /// <summary>Stops routing the Crowd Control log to the Unity console once no instance needs it.</summary>
        private void UnhookLogging()
        {
            if (!m_loggingHooked) return;
            m_loggingHooked = false;

            if (Interlocked.Decrement(ref s_loggingSubscribers) != 0) return;

            Log.OnMessage -= OnLogMessage;
            CrowdControlLogFile.Close();
        }

        void Awake()
        {
            if (PreserveBetweenScenes) DontDestroyOnLoad(gameObject);

            HookLogging();

            m_synchronizationContext = SynchronizationContext.Current;
            m_taskScheduler = new(m_synchronizationContext);
            foreach (IMetadata metadata in MetadataLoader?.Metadata.Values ?? Array.Empty<UnityMetadataBase>())
            {
                metadata.Updated += () =>
                {
                    if (CrowdControl == null) return;
                    CrowdControl.UpdateMetadata(metadata.Key, metadata.Value);
                };
            }
        }

        /// <summary>Unity callback invoked when the component is enabled. Initializes and connects the Crowd Control client if autoConnect is enabled.</summary>
        void Start()
        {
            if (!GameStateManager)
            {
                Debug.LogError("CrowdControlBehavior.GameStateManager is not set! Please set it before enabling the CrowdControl Behavior.");
                enabled = false;
                return;
            }

            if (!EffectLoader)
            {
                Debug.LogError("CrowdControlBehavior.EffectLoader is not set! Please set it before enabling the CrowdControl Behavior.");
                enabled = false;
                return;
            }

            m_jwt = PlayerPrefs.GetString("CrowdControl_JWT", null);

            if (AutoConnect) Connect();
        }

        /// <summary>Stops and disposes the Crowd Control client instance, if any.</summary>
        void Stop()
        {
            if (CrowdControl == null) return;

            CrowdControl.EffectRequestReceived -= OnEffectRequestReceived;
            CrowdControl.EffectResponseSent -= OnEffectResponseSent;
            CrowdControl.EffectReportSent -= OnEffectReportSent;

            CrowdControl.AuthCodeReceived -= OnAuthCodeReceived;
            CrowdControl.AuthCodeRedeemedReceived -= OnAuthCodeRedeemedReceived;
            CrowdControl.AuthCodeErrorReceived -= OnAuthCodeErrorReceived;
            CrowdControl.SessionReady -= OnSessionReady;
            CrowdControl.SessionEnded -= OnSessionEnded;

            try { CrowdControl?.Dispose(); }
            catch { /**/ }
            try { CrowdControl = null; }
            catch { /**/ }

            OnSessionEnded();
        }

        /// <summary>Initializes and connects the Crowd Control client.</summary>
        /// <remarks>
        /// If a client already exists it is stopped first. Overwriting it instead would orphan a live client that
        /// still holds the shared <see cref="EffectLoader"/> and <see cref="MetadataLoader"/>, and its disposal would
        /// then run from the finalizer at an arbitrary later point, tearing down state the replacement is using.
        /// </remarks>
        public void Connect()
        {
            if (!enabled)
            {
                Debug.LogError("CrowdControlBehavior is not enabled! Cannot connect to Crowd Control.");
                return;
            }

            if (CrowdControl != null)
            {
                Log.Debug("Connect called while a client already exists; stopping the existing client first.");
                Stop();
            }

            CrowdControl = new WebSocket.CrowdControl(GameStateManager, EffectLoader, MetadataLoader, m_taskScheduler, GameID, ApplicationID, m_jwt);
            CrowdControl.LoadContent();
            CrowdControl.EffectRequestReceived += OnEffectRequestReceived;
            CrowdControl.EffectResponseSent += OnEffectResponseSent;
            CrowdControl.EffectReportSent += OnEffectReportSent;

            CrowdControl.AuthCodeReceived += OnAuthCodeReceived;
            CrowdControl.AuthCodeRedeemedReceived += OnAuthCodeRedeemedReceived;
            CrowdControl.AuthCodeErrorReceived += OnAuthCodeErrorReceived;
            CrowdControl.JwtTokenReceived += j =>
            {
                m_jwt = j;
                if (PersistLoginToken)
                {
                    m_synchronizationContext?.Post(_ =>
                    {
                        Log.Debug("Persisting JWT token...");
                        PlayerPrefs.SetString("CrowdControl_JWT", j);
                        PlayerPrefs.Save();
                    }, null);
                }
                OnLoginTokenReceived(j);
            };

            CrowdControl.SessionReady += OnSessionReady;
            CrowdControl.SessionEnded += OnSessionEnded;

            CrowdControl.AutoReconnect = AutoReconnect;
            CrowdControl.LogSocketTraffic = LogSocketTraffic;
            CrowdControl.Connect();
            RefreshJWT();
        }

        private void RefreshJWT()
        {
            CrowdControl?.RefreshToken();
            if (CrowdControl.IsTokenValid())
            {
                Log.Debug("Valid JWT token found, attempting to start session...");
                Task.Run(async () =>
                {
                    if (!(await CrowdControl.StartSession(false)))
                        await CrowdControl.GetAuthCode();
                }).Forget();
            }
            else
            {
                Log.Debug("No valid JWT token found, requesting authentication code...");
                CrowdControl.GetAuthCode().Forget();
            }
        }

        /// <summary>Launches the interact link URL in the user's default web browser.</summary>
        public void LaunchInteractLink()
        {
            if (CrowdControl == null)
            {
                Debug.LogError("CrowdControlBehavior is not connected! Cannot launch interact link.");
                return;
            }
            string? url = CrowdControl.GetInteractLink();
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError("Failed to get interact link from Crowd Control.");
                return;
            }
            Application.OpenURL(url);
        }

        /// <summary>Gets the interact link URL from the Crowd Control client.</summary>
        public string? GetInteractLink()
        {
            if (CrowdControl == null)
            {
                Debug.LogError("CrowdControlBehavior is not connected! Cannot get interact link.");
                return null;
            }
            return CrowdControl.GetInteractLink();
        }

        /// <summary>Disconnects from the Crowd Control service and disposes the client instance.</summary>
        public void Disconnect() => Stop();

        /// <summary>Gets a value indicating whether there is a valid JWT token stored for authentication with the Crowd Control service.</summary>
        public static bool IsStoredTokenValid
            => WebSocket.CrowdControl.IsTokenValid(PlayerPrefs.GetString("CrowdControl_JWT", null));

        /// <summary>Clears the stored JWT token, forcing a full re-authentication on the next connection attempt.</summary>
        public static void ClearStoredToken()
        {
            PlayerPrefs.DeleteKey("CrowdControl_JWT");
            PlayerPrefs.Save();
        }

        /// <summary>Gets a value indicating whether the currently stored JWT token is valid for authentication with the Crowd Control service.</summary>
        public bool IsTokenValid => WebSocket.CrowdControl.IsTokenValid(m_jwt);

        /// <summary>Clears the stored JWT token, forcing a full re-authentication on the next connection attempt.</summary>
        public void ClearToken()
        {
            m_synchronizationContext?.Post(_ =>
            {
                m_jwt = null;
                ClearStoredToken();
            }, null);
        }

        /// <summary>UnityEvent invoked when the Crowd Control session is ready. This can be used to trigger in-game responses to the session being ready.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="AuthCodeReceived"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        [Tooltip("Invoked when the Crowd Control session is ready. This can be used to trigger in-game responses to the session being ready.")]
        public UnityEvent? SessionReadyEvent;

        /// <summary>Event invoked when the Crowd Control session is ready. This can be used to trigger in-game responses to the session being ready.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="SessionReadyEvent"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once EventNeverSubscribedTo.Global
        public event Action? SessionReady;

        private void OnSessionReady()
        {
            Debug.Log("Crowd Control session is ready.");
            m_synchronizationContext?.Post(_ =>
            {
                SessionReady.InvokeSafe();
                SessionReadyEvent?.Invoke();
            }, null);
#if UNITY_EDITOR
            //deliberately compiled out of player builds: a shipped game must never rewrite its game pack effect list
            if (AutoAddCustomEffects)
                UploadCustomEffects(CustomEffects.OperationMode.ReplacePartial).Forget();
#endif
        }

        /// <summary>UnityEvent invoked when the Crowd Control session has ended. This can be used to trigger in-game responses to the session ending.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="SessionEnded"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        [Tooltip("Invoked when the Crowd Control session has ended. This can be used to trigger in-game responses to the session ending.")]
        public UnityEvent? SessionEndedEvent;

        /// <summary>Event invoked when the Crowd Control session is ready. This can be used to trigger in-game responses to the session being ready.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="SessionEndedEvent"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once EventNeverSubscribedTo.Global
        public event Action? SessionEnded;

        private void OnSessionEnded()
        {
            Debug.Log("Crowd Control session has ended.");
            m_synchronizationContext?.Post(_ =>
            {
                SessionEnded.InvokeSafe();
                SessionEndedEvent?.Invoke();
            }, null);
            StopAllEffects().Forget();
        }

        /// <summary>UnityEvent invoked whenever an authentication code is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="AuthCodeReceived"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        [Tooltip("Invoked whenever an authentication code is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.")]
        public UnityEvent<ApplicationAuthCode>? AuthCodeReceivedEvent;

        /// <summary>Event invoked whenever an authentication code is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="AuthCodeReceivedEvent"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once EventNeverSubscribedTo.Global
        public event Action<ApplicationAuthCode>? AuthCodeReceived;

        private void OnAuthCodeReceived(ApplicationAuthCode authCode)
        {
            Debug.Log($"Authentication code received: {authCode.Code}, URL: {authCode.Url}");
            m_synchronizationContext?.Post(_ =>
            {
                AuthCodeReceived.InvokeSafe(authCode);
                AuthCodeReceivedEvent?.Invoke(authCode);
            }, null);
        }

        /// <summary>UnityEvent invoked whenever an authentication code redemption result is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="AuthCodeRedeemedReceived"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        [Tooltip("Invoked whenever an authentication code redemption result is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.")]
        public UnityEvent<ApplicationAuthCodeRedeemed>? AuthCodeRedeemedReceivedEvent;

        /// <summary>Event invoked whenever an authentication code redemption result is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="AuthCodeRedeemedReceivedEvent"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once EventNeverSubscribedTo.Global
        public event Action<ApplicationAuthCodeRedeemed>? AuthCodeRedeemedReceived;

        private void OnAuthCodeRedeemedReceived(ApplicationAuthCodeRedeemed authCodeRedeemed)
        {
            Debug.Log($"Authentication code redeemed: {authCodeRedeemed.Code}");
            m_synchronizationContext?.Post(_ =>
            {
                AuthCodeRedeemedReceived.InvokeSafe(authCodeRedeemed);
                AuthCodeRedeemedReceivedEvent?.Invoke(authCodeRedeemed);
            }, null);
        }

        /// <summary>UnityEvent invoked whenever an authentication code error is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="AuthCodeErrorReceived"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        [Tooltip("Invoked whenever an authentication code error is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.")]
        public UnityEvent<ApplicationAuthCodeError>? AuthCodeErrorReceivedEvent;

        /// <summary>Event invoked whenever an authentication code error is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="AuthCodeErrorReceivedEvent"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once EventNeverSubscribedTo.Global
        public event Action<ApplicationAuthCodeError>? AuthCodeErrorReceived;

        private void OnAuthCodeErrorReceived(ApplicationAuthCodeError authCodeError)
        {
            Debug.LogError($"Authentication code error received: {authCodeError.Message}");
            m_synchronizationContext?.Post(_ =>
            {
                AuthCodeErrorReceived.InvokeSafe(authCodeError);
                AuthCodeErrorReceivedEvent?.Invoke(authCodeError);
            }, null);
        }

        /// <summary>UnityEvent invoked whenever a JWT login token is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="LoginTokenReceived"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        [Tooltip("Invoked whenever a JWT login token is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.")]
        public UnityEvent<string>? LoginTokenReceivedEvent;

        /// <summary>Event invoked whenever a JWT login token is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="LoginTokenReceivedEvent"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once EventNeverSubscribedTo.Global
        public event Action<string>? LoginTokenReceived;

        private void OnLoginTokenReceived(string token)
        {
            m_synchronizationContext?.Post(_ =>
            {
                LoginTokenReceived.InvokeSafe(token);
                LoginTokenReceivedEvent?.Invoke(token);
            }, null);
        }

        /// <summary>UnityEvent invoked whenever an effect request is received from the Crowd Control service. This can be used to trigger in-game responses to effect requests.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="EffectReceived"/> event, but not both, to avoid duplicate handling of effect requests.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        [Tooltip("Invoked whenever an effect request is received from the Crowd Control service. This can be used to trigger in-game responses to effect requests.")]
        public UnityEvent<EffectRequest>? EffectReceivedEvent;

        /// <summary>Event invoked whenever an effect request is received from the Crowd Control service. This can be used to trigger in-game responses to effect requests.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="EffectReceivedEvent"/> event, but not both, to avoid duplicate handling of effect requests.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        public event Action<EffectRequest>? EffectReceived;

        private void OnEffectRequestReceived(EffectRequest effectRequest)
        {
            m_synchronizationContext?.Post(_ =>
            {
                EffectReceived.InvokeSafe(effectRequest);
                EffectReceivedEvent?.Invoke(effectRequest);
            }, null);
        }

        /// <summary>UnityEvent invoked whenever an effect class update is sent to the Crowd Control service. This can be used to trigger in-game responses to effect class updates.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="EffectUpdate"/> event, but not both, to avoid duplicate handling of effect updates.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        [Tooltip("Invoked whenever an effect class update is sent to the Crowd Control service. This can be used to trigger in-game responses to effect class updates.")]
        public UnityEvent<EffectState>? EffectUpdateEvent;

        /// <summary>Event invoked whenever an effect class update is sent to the Crowd Control service. This can be used to trigger in-game responses to effect class updates.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="EffectUpdateEvent"/> event, but not both, to avoid duplicate handling of effect updates.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once EventNeverSubscribedTo.Global
        public event Action<EffectState>? EffectUpdate;

        /// <summary>Occurs when one or more metadata values are sent to the Crowd Control service.</summary>
        public event Action<IEnumerable<KeyValuePair<string, object?>>>? MetadataChanged;

        private void OnEffectResponseSent(EffectRequest effectRequest, Common.EffectResponse effectResponse)
        {
            if (CrowdControl == null) return;
            if (!CrowdControl.EffectLoader.Effects.TryGetValue(effectRequest.EffectID, out var effect)) return;
            EffectState state = new(effect, effectRequest, effectResponse);
            m_synchronizationContext?.Post(_ =>
            {
                EffectUpdate.InvokeSafe(state);
                EffectUpdateEvent?.Invoke(state);
            }, null);
        }

        private void OnEffectReportSent(EffectReport effectReport) { }

        /// <summary>Sends a ping to the Crowd Control service and logs the result.</summary>
        public void Ping()
        {
            if (!Connected)
            {
                Debug.LogError("CrowdControlBehavior is not connected! Cannot ping Crowd Control.");
                return;
            }
            System.Diagnostics.Debug.Assert(CrowdControl != null);

            //never block here: the ping completes on a background thread, and waiting on the main thread deadlocks it
            CrowdControl.Ping().ContinueWith(t => printResult(t.Status == TaskStatus.RanToCompletion && t.Result));

            void printResult(bool success)
            {
                m_synchronizationContext?.Post(_ =>
                {
                    if (success) Debug.Log($"Ping response received.");
                    else Debug.LogError("Ping failed to receive a response.");
                }, null);
            }
        }

        /// <summary>Unity physics update loop; forwards timing to the Crowd Control client for processing.</summary>
        void FixedUpdate() => CrowdControl?.Update(Time.time, Time.deltaTime);

        /// <summary>Unity callback invoked when the component is destroyed; ensures disposal.</summary>
        void OnDestroy() => Dispose();

        /// <summary>This method attempts to stop all running effects via the Crowd Control effect scheduler.</summary>
        /// <remarks>This will return false if any of the effect stop functions throw an exception.</remarks>
        public Task<bool> StopAllEffects()
        {
            TaskCompletionSource<bool> tcs = new();
            m_synchronizationContext?.Post(_ =>
            {
                bool result = CrowdControl?.Scheduler.StopAll() ?? false;
                tcs.SetResult(result);
            }, null);
            return tcs.Task;
        }

        /// <summary>
        /// Attempts to get a metadata object from the Crowd Control client.
        /// </summary>
        /// <typeparam name="TValue">The expected type of the metadata value.</typeparam>
        /// <param name="key">The key of the metadata to retrieve.</param>
        /// <param name="value">When this method returns, contains the metadata object associated with the specified key, if the key is found and the value can be cast to <typeparamref name="TValue"/>; otherwise, null.</param>
        /// <returns>true if the metadata object was found and successfully cast to <typeparamref name="TValue"/>; otherwise, false.</returns>
        /// <remarks>This method will return false if the metadata loader is not initialized, if the specified key does not exist in the metadata, or if the value cannot be cast to <typeparamref name="TValue"/>.</remarks>
        public bool TryGetMetadataObject<TValue>(string key, out TValue value)
        {
            value = default!;
            if (MetadataLoader == null) return false;
            if (!MetadataLoader.Metadata.TryGetValue(key, out IMetadata metadata)) return false;
            if (metadata is TValue typedMetadata)
            {
                value = typedMetadata;
                return true;
            }
            return false;
        }

        /// <summary>Attempts to retrieve a metadata value from the Crowd Control client.</summary>
        /// <typeparam name="TValue">The expected type of the metadata value.</typeparam>
        /// <param name="key">The key of the metadata value to retrieve.</param>
        /// <param name="value">When this method returns, contains the metadata value associated with the specified key, if the key is found and the value can be cast to <typeparamref name="TValue"/>; otherwise, the default value for <typeparamref name="TValue"/>.</param>
        /// <returns>true if the metadata value was found and successfully cast to <typeparamref name="TValue"/>; otherwise, false.</returns>
        /// <remarks>This method will return false if the metadata loader is not initialized, if the specified key does not exist in the metadata, or if the value cannot be cast to <typeparamref name="TValue"/>.</remarks>
        public bool TryGetMetadataValue<TValue>(string key, out TValue value)
        {
            value = default!;
            if (MetadataLoader == null) return false;
            if (!MetadataLoader.Metadata.TryGetValue(key, out IMetadata metadata)) return false;
            if (metadata is not IMetadata<TValue> typedMetadata) return false;
            value = typedMetadata.Value;
            return true;
        }

        /// <summary>Attempts to retrieve a metadata value from the Crowd Control client.</summary>
        /// <typeparam name="TValue">The expected type of the metadata value.</typeparam>
        /// <param name="key">The key of the metadata value to retrieve.</param>
        /// <param name="value">When this method returns, contains the metadata value associated with the specified key, if the key is found and the value can be cast to <typeparamref name="TValue"/>; otherwise, the default value for <typeparamref name="TValue"/>.</param>
        /// <returns>true if the metadata value was found and successfully cast to <typeparamref name="TValue"/>; otherwise, false.</returns>
        /// <remarks>This method will return false if the metadata loader is not initialized, if the specified key does not exist in the metadata, or if the value cannot be cast to <typeparamref name="TValue"/>.</remarks>
        public bool TryGetMetadataString(string key, out string value)
        {
            value = default!;
            if (MetadataLoader == null) return false;
            if (!MetadataLoader.Metadata.TryGetValue(key, out IMetadata metadata)) return false;
            value = metadata.Value?.ToString() ?? string.Empty;
            return true;
        }

        #region Custom Effects

        /*
         * Everything in this region is a DEVELOPMENT TOOL.
         *
         * Uploading custom effects rewrites the effect list that the Crowd Control service hands out for this game
         * pack, so it affects every viewer of the game, not just the machine that made the call. The service only
         * permits it with a developer token carrying the "custom-effects:write" scope, which ordinary player tokens
         * never have, and only for game packs configured with "allowCustomEffects": true.
         *
         * The intended workflow is to iterate on effects from the Unity Editor, then publish the finished menu in the
         * game pack. A released build should never call any of this.
         *
         * See https://developer.crowdcontrol.live/sockets/#custom-effects
         */

        /// <summary>
        /// Gets a value indicating whether custom effects can currently be uploaded to or removed from the service.
        /// </summary>
        /// <remarks>
        /// This is <see langword="true"/> only when the client is connected with a developer token that carries the
        /// <c>custom-effects:write</c> scope. It is always <see langword="false"/> for an ordinary player token, so a
        /// released build cannot modify the game pack even if it tries.
        /// </remarks>
        public bool CanUploadCustomEffects => CrowdControl?.CanWriteCustomEffects ?? false;

        /// <summary>
        /// Updates custom effects in the Crowd Control system by loading effects from the configured effect loader
        /// using merge mode.
        /// </summary>
        /// <remarks>
        /// <b>Development tool.</b> See <see cref="UploadCustomEffects"/> for the requirements and caveats. This
        /// overload is fire-and-forget so it can be wired directly to a UnityEvent or an inspector button; use
        /// <see cref="UploadCustomEffects"/> when the result is needed.
        /// </remarks>
        public void UpdateCustomEffects() => UploadCustomEffects(CustomEffects.OperationMode.Merge).Forget();

        /// <summary>
        /// Uploads the custom effects exposed by the configured effect loader to the Crowd Control service.
        /// </summary>
        /// <param name="mode">
        /// How the uploaded effects combine with the effects already registered for the game pack.
        /// <see cref="CustomEffects.OperationMode.Merge"/> adds to them, <see cref="CustomEffects.OperationMode.ReplacePartial"/>
        /// overwrites just the supplied effects, and <see cref="CustomEffects.OperationMode.ReplaceAll"/> discards
        /// everything that is not supplied.
        /// </param>
        /// <returns>A task that resolves to <see langword="true"/> if the upload succeeded; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// <b>Development tool. Do not call this from a released build.</b> It requires a connected client with a
        /// developer token carrying the <c>custom-effects:write</c> scope, an assigned
        /// <see cref="EffectLoader"/>, and a game pack configured to allow custom effects. Check
        /// <see cref="CanUploadCustomEffects"/> first. Only effects with <see cref="UnityEffectBase.CustomEffect"/>
        /// set are uploaded, and the service accepts at most 75 of them per game pack.
        /// </remarks>
        public async Task<bool> UploadCustomEffects(CustomEffects.OperationMode mode)
        {
            if (!TryGetCustomEffectContext("upload custom effects", out WebSocket.CrowdControl? crowdControl, out UnityEffectLoader? effectLoader))
                return false;

            //snapshot on the calling (main) thread: the registry is a scene-derived collection and must not be
            //enumerated from the background task that performs the upload
            List<IEffect> customEffects = effectLoader!.Effects.Values.Where(e => e.IsCustom).ToList();
            if (customEffects.Count == 0)
            {
                Debug.LogWarning("No custom effects were found to upload. Effects are only uploaded when their Custom Effect flag is set.");
                return false;
            }

            bool success = await Task.Run(() => crowdControl!.LoadCustomEffects(customEffects, mode)).ConfigureAwait(false);
            LogCustomEffectResult(success, $"Uploaded {customEffects.Count} custom effect(s).", "Failed to upload custom effects.");
            return success;
        }

        /// <summary>Removes specific custom effects from the Crowd Control service by their effect IDs.</summary>
        /// <param name="effectIDs">The effect IDs to remove.</param>
        /// <returns>A task that resolves to <see langword="true"/> if the removal succeeded; otherwise, <see langword="false"/>.</returns>
        /// <remarks><b>Development tool.</b> See <see cref="UploadCustomEffects"/> for the requirements and caveats.</remarks>
        public Task<bool> DeleteCustomEffects(params string[] effectIDs) => DeleteCustomEffects((IEnumerable<string>)effectIDs);

        /// <summary>Removes specific custom effects from the Crowd Control service by their effect IDs.</summary>
        /// <param name="effectIDs">The effect IDs to remove.</param>
        /// <returns>A task that resolves to <see langword="true"/> if the removal succeeded; otherwise, <see langword="false"/>.</returns>
        /// <remarks><b>Development tool.</b> See <see cref="UploadCustomEffects"/> for the requirements and caveats.</remarks>
        public async Task<bool> DeleteCustomEffects(IEnumerable<string> effectIDs)
        {
            if (!TryGetCustomEffectContext("delete custom effects", out WebSocket.CrowdControl? crowdControl, out _, requireEffectLoader: false))
                return false;

            List<string> ids = effectIDs.ToList();
            bool success = await Task.Run(() => crowdControl!.DeleteCustomEffects(ids)).ConfigureAwait(false);
            LogCustomEffectResult(success, $"Removed {ids.Count} custom effect(s).", "Failed to remove custom effects.");
            return success;
        }

        /// <summary>Removes every custom effect registered for this game pack from the Crowd Control service.</summary>
        /// <returns>A task that resolves to <see langword="true"/> if the removal succeeded; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// <b>Development tool, and a destructive one.</b> This clears the whole custom effect list for the game pack
        /// on the service, including effects uploaded from another machine. See <see cref="UploadCustomEffects"/> for
        /// the requirements and caveats.
        /// </remarks>
        public async Task<bool> DeleteAllCustomEffects()
        {
            if (!TryGetCustomEffectContext("delete custom effects", out WebSocket.CrowdControl? crowdControl, out _, requireEffectLoader: false))
                return false;

            bool success = await Task.Run(() => crowdControl!.DeleteAllCustomEffects()).ConfigureAwait(false);
            LogCustomEffectResult(success, "Removed all custom effects.", "Failed to remove custom effects.");
            return success;
        }

        /// <summary>Validates the prerequisites shared by every custom effect operation, logging the reason on failure.</summary>
        /// <param name="action">The action being attempted, used in the log message.</param>
        /// <param name="crowdControl">The connected client, when this returns <see langword="true"/>.</param>
        /// <param name="effectLoader">The assigned effect loader, when this returns <see langword="true"/>.</param>
        /// <param name="requireEffectLoader">Whether the operation needs an effect loader. Removals do not, since they work from IDs alone.</param>
        /// <returns><see langword="true"/> if the operation may proceed; otherwise, <see langword="false"/>.</returns>
        private bool TryGetCustomEffectContext(string action, out WebSocket.CrowdControl? crowdControl, out UnityEffectLoader? effectLoader, bool requireEffectLoader = true)
        {
            crowdControl = CrowdControl;
            effectLoader = EffectLoader;

            if (crowdControl == null)
            {
                Debug.LogError($"CrowdControlBehavior is not connected! Cannot {action}.");
                return false;
            }

            if (requireEffectLoader && (!effectLoader))
            {
                Debug.LogError($"CrowdControlBehavior.EffectLoader is not set! Please set it before attempting to {action}.");
                return false;
            }

            if (!crowdControl.CanWriteCustomEffects)
            {
                Debug.LogError($"Cannot {action}: the current login does not have the 'custom-effects:write' scope. " +
                               "Custom effect uploading is a development tool and needs a developer token issued by Crowd Control. " +
                               "Released games should publish their effects in their game pack menu instead.");
                return false;
            }

            return true;
        }

        /// <summary>Reports the outcome of a custom effect operation on the Unity main thread.</summary>
        private void LogCustomEffectResult(bool success, string successMessage, string failureMessage)
            => m_synchronizationContext?.Post(_ =>
            {
                if (success) Debug.Log(successMessage);
                else Debug.LogError(failureMessage);
            }, null);

        #endregion

        /// <inheritdoc cref="WebSocket.CrowdControl.CloneEffect"/>
        public bool CloneEffect(string sourceEffectID, params string[] destEffectIDs) => CrowdControl?.CloneEffect(sourceEffectID, destEffectIDs) ?? false;

        #region Show Effects

        /// <inheritdoc cref="WebSocket.CrowdControl.ShowEffects(string[])"/>
        public Task<bool> ShowEffects(params string[] codes) => CrowdControl?.ShowEffects(codes) ?? Task.FromResult(false);

        /// <inheritdoc cref="WebSocket.CrowdControl.ShowEffects(IEnumerable{string}, string?)"/>
        public Task<bool> ShowEffects(IEnumerable<string> codes, string? message = null) => CrowdControl?.ShowEffects(codes, message) ?? Task.FromResult(false);

        /// <inheritdoc cref="WebSocket.CrowdControl.ShowAllEffects(string?)"/>
        public Task<bool> ShowAllEffects(string? message = null) => CrowdControl?.ShowAllEffects(message) ?? Task.FromResult(false);

        #endregion

        #region Hide Effects

        /// <inheritdoc cref="WebSocket.CrowdControl.HideEffects(string[])"/>
        public Task<bool> HideEffects(params string[] codes) => CrowdControl?.HideEffects(codes) ?? Task.FromResult(false);

        /// <inheritdoc cref="WebSocket.CrowdControl.HideEffects(IEnumerable{string}, string?)"/>
        public Task<bool> HideEffects(IEnumerable<string> codes, string? message = null) => CrowdControl?.HideEffects(codes, message) ?? Task.FromResult(false);

        /// <inheritdoc cref="WebSocket.CrowdControl.HideAllEffects(string?)"/>
        public Task<bool> HideAllEffects(string? message = null) => CrowdControl?.HideAllEffects(message) ?? Task.FromResult(false);

        #endregion

        #region Enable Effects

        /// <inheritdoc cref="WebSocket.CrowdControl.EnableEffects(string[])"/>
        public Task<bool> EnableEffects(params string[] codes) => CrowdControl?.EnableEffects(codes) ?? Task.FromResult(false);

        /// <inheritdoc cref="WebSocket.CrowdControl.EnableEffects(IEnumerable{string}, string?)"/>
        public Task<bool> EnableEffects(IEnumerable<string> codes, string? message = null) => CrowdControl?.EnableEffects(codes, message) ?? Task.FromResult(false);

        /// <inheritdoc cref="WebSocket.CrowdControl.EnableAllEffects(string?)"/>
        public Task<bool> EnableAllEffects(string? message = null) => CrowdControl?.EnableAllEffects(message) ?? Task.FromResult(false);

        #endregion

        #region Disable Effects

        /// <inheritdoc cref="WebSocket.CrowdControl.DisableEffects(string[])"/>
        public Task<bool> DisableEffects(params string[] codes) => CrowdControl?.DisableEffects(codes) ?? Task.FromResult(false);

        /// <inheritdoc cref="WebSocket.CrowdControl.DisableEffects(IEnumerable{string}, string?)"/>
        public Task<bool> DisableEffects(IEnumerable<string> codes, string? message = null) => CrowdControl?.DisableEffects(codes, message) ?? Task.FromResult(false);

        /// <inheritdoc cref="WebSocket.CrowdControl.DisableAllEffects(string?)"/>
        public Task<bool> DisableAllEffects(string? message = null) => CrowdControl?.DisableAllEffects(message) ?? Task.FromResult(false);

        #endregion
    }
}