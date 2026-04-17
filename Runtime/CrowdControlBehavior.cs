using CrowdControl.Client.WebSocket;
using CrowdControl.Client.WebSocket.Data;
using CrowdControl.Client.WebSocket.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using CrowdControl.Common;

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

        /// <summary>The application secret used for authentication with the Crowd Control service.</summary>
        [SerializeField]
        [Tooltip("The application secret used for authentication with the Crowd Control service.")]
        public string ApplicationSecret;

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

        /// <summary>Whether to block on ping responses.</summary>
        /// <remarks>This is for testing purposes only and should generally be false in production.</remarks>
        [SerializeField]
        [Tooltip("Whether to block on ping responses. This is for testing purposes only and should generally be false in production.")]
        public bool WaitForPingResponse = false;

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
            if (disposing) GC.SuppressFinalize(this);
        }

        void Awake()
        {
            if (PreserveBetweenScenes) DontDestroyOnLoad(gameObject);

            Debug.Log("Rerouting Crowd Control logs to Unity console...");
            Log.FileOutput = false;
            Log.ConsoleOutput = false;
            Log.OnMessage += (message, level) =>
            {
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
            };

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
        public void Connect()
        {
            if (!enabled)
            {
                Debug.LogError("CrowdControlBehavior is not enabled! Cannot connect to Crowd Control.");
                return;
            }
            CrowdControl = new WebSocket.CrowdControl(GameStateManager, EffectLoader, MetadataLoader, m_taskScheduler, GameID, ApplicationID, ApplicationSecret, m_jwt);
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
            };

            CrowdControl.SessionReady += OnSessionReady;
            CrowdControl.SessionEnded += OnSessionEnded;

            CrowdControl.Connect();

            if (CrowdControl.IsTokenValid())
            {
                Log.Debug("Valid JWT token found, attempting to start session...");
                Task.Run(async () =>
                {
                    if (!(await CrowdControl.StartSession()))
                        await CrowdControl.GetAuthCode();
                }).Forget();
            }
            else
            {
                Log.Debug("No valid JWT token found, requesting authentication code...");
                CrowdControl.GetAuthCode().Forget();
            }
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
        public event Action<IEnumerable<KeyValuePair<string, object?>>>? MetadataChanged;

        private void OnEffectResponseSent(EffectRequest effectRequest, EffectResponse effectResponse)
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
            Task<bool> result = CrowdControl.Ping();
            if (WaitForPingResponse)
            {
                result.Wait();
                printResult(result.Result);
            }
            else
            {
                result.ContinueWith(t => printResult(t.Result));
            }
            
            void printResult(bool success)
            {
                if (success) Debug.Log($"Ping response received.");
                else Debug.LogError("Ping failed to receive a response.");   
            }
        }
    
        /// <summary>Unity physics update loop; forwards timing to the Crowd Control client for processing.</summary>
        void FixedUpdate()
        {
            CrowdControl?.Update(Time.time, Time.deltaTime);
        }
    
        /// <summary>Unity callback invoked when the component is destroyed; ensures disposal.</summary>
        void OnDestroy() => Dispose();

        /// <summary>This method attempts to stop all running effects via the Crowd Control effect scheduler.</summary>
        /// <remarks>This will return false if any of the effect stop functions throw an exception.</remarks>
        public bool StopAllEffects()
        {
            return CrowdControl?.Scheduler.StopAll() ?? false;
        }
    }
}