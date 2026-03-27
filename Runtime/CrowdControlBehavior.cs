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
    public class CrowdControlBehavior : MonoBehaviour, IDisposable, IEffectPack
    {
        /// <summary>The game identifier used when connecting to the Crowd Control service.</summary>
        [SerializeField]
        public string GameID;

        /// <summary>The application identifier used for authentication with the Crowd Control service.</summary>
        [SerializeField]
        public string ApplicationID;

        /// <summary>The application secret used for authentication with the Crowd Control service.</summary>
        [SerializeField]
        public string ApplicationSecret;

        /// <summary>The display name used when connecting to the Crowd Control service.</summary>
        [SerializeField]
        public string DisplayName;

        /// <summary>Component that provides the current <see cref="WebSocket.GameState"/> to Crowd Control.</summary>
        [SerializeField]
        public UnityGameStateManager? GameStateManager;

        /// <summary>Component responsible for finding and registering <see cref="UnityEffectBase"/> instances.</summary>
        [SerializeField]
        public UnityEffectLoader? EffectLoader;

        /// <summary>Component responsible for finding and registering <see cref="UnityMetadataBase"/> instances.</summary>
        [SerializeField]
        public UnityMetadataLoader? MetadataLoader;

        /// <summary>Whether to automatically connect to Crowd Control on start.</summary>
        [SerializeField]
        public bool autoConnect = true;
        
        /// <summary>Whether to block on ping responses.</summary>
        /// <remarks>This is for testing purposes only and should generally be false in production.</remarks>
        [SerializeField]
        public bool waitForPingResponse = false;
        
        /// <summary>Gets a value indicating whether the Crowd Control client is currently connected.</summary>
        /// <remarks>True if the client is created and connected; false otherwise.</remarks>
        public bool Connected => m_crowdControl?.Connected ?? false;

        private SynchronizationContext? m_synchronizationContext;
    
        [NonSerialized]
        private WebSocket.CrowdControl? m_crowdControl;

        #region IEffectPack

        Game IEffectPack.Game => new Game(DisplayName, GameID);

        public List<(string, Action)> MenuActions { get; } = new();

        public UISettings UISettings { get; } = new UISettings();

        public EffectList Effects => (EffectList?)EffectLoader?.Effects.Values.Select(e => e.ToEffect()) ?? new();

        public EffectList ConnectorEffects { get; } = new();

        public IEnumerable<Effect> SupportedEffects => Effects;

        public IEnumerable<Effect> UnsupportedEffects { get; } = Array.Empty<Effect>();

        public SITimeSpan GlobalMinimumDelay { get; set; } = SITimeSpan.Zero;

        public IDictionary<string, object> MetadataDefaults => throw new NotImplementedException();

        #endregion

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
            foreach (IMetadata metadata in MetadataLoader?.Metadata.Values ?? Array.Empty<UnityMetadataBase>())
            {
                metadata.Updated += () =>
                {
                    if (m_crowdControl == null) return;
                    m_crowdControl.UpdateMetadata(metadata.Key, metadata.Value);
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

            if (autoConnect) Connect();
        }
        
        /// <summary>Initializes and connects the Crowd Control client.</summary>
        public void Connect()
        {
            if (!enabled)
            {
                Debug.LogError("CrowdControlBehavior is not enabled! Cannot connect to Crowd Control.");
                return;
            }
            m_crowdControl = WebSocket.CrowdControl.Create(GameStateManager, EffectLoader, MetadataLoader, GameID, ApplicationID, ApplicationSecret);
            m_crowdControl.LoadContent();
            m_crowdControl.EffectRequestReceived += OnEffectRequestReceived;
            m_crowdControl.EffectResponseSent += OnEffectResponseSent;
            m_crowdControl.EffectReportSent += OnEffectReportSent;

            m_crowdControl.AuthCodeReceived += OnAuthCodeReceived;
            m_crowdControl.AuthCodeRedeemedReceived += OnAuthCodeRedeemedReceived;
            m_crowdControl.AuthCodeErrorReceived += OnAuthCodeErrorReceived;

            if (m_crowdControl == null) return;
            m_crowdControl.Connect();
            m_crowdControl.GetAuthCode();
        }

        /// <summary>UnityEvent invoked whenever an authentication code is received from the Crowd Control service. This can be used to trigger in-game responses to authentication events.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="AuthCodeReceived"/> event, but not both, to avoid duplicate handling of authentication events.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        /// ReSharper disable once UnassignedField.Global
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

        /// <summary>UnityEvent invoked whenever an effect request is received from the Crowd Control service. This can be used to trigger in-game responses to effect requests.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="EffectReceived"/> event, but not both, to avoid duplicate handling of effect requests.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
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

        /// <summary>UnityEvent invoked whenever an effect response is sent to the Crowd Control service. This can be used to trigger in-game responses to effect updates.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="EffectUpdate"/> event, but not both, to avoid duplicate handling of effect responses.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once UnassignedField.Global
        public UnityEvent<EffectState>? EffectUpdateEvent;
        
        /// <summary>Event invoked whenever an effect response is sent to the Crowd Control service. This can be used to trigger in-game responses to effect updates.</summary>
        /// <remarks>Note that this event is invoked on the Unity main thread, so it's safe to perform Unity operations in response to it.</remarks>
        /// <remarks>Subscribers should use either this event or the <see cref="EffectUpdateEvent"/> event, but not both, to avoid duplicate handling of effect responses.</remarks>
        /// <remarks>This event is invoked between Update() and LateUpdate() in the Unity lifecycle, so it will be processed after all Update() calls but before any LateUpdate() calls.</remarks>
        // ReSharper disable once EventNeverSubscribedTo.Global
        public event Action<EffectState>? EffectUpdate;
        public event Action<IEnumerable<KeyValuePair<string, object?>>>? MetadataChanged;

        private void OnEffectResponseSent(EffectRequest effectRequest, EffectResponse effectResponse)
        {
            if (m_crowdControl == null) return;
            if (!m_crowdControl.EffectLoader.Effects.TryGetValue(effectRequest.EffectID, out var effect)) return;
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
            System.Diagnostics.Debug.Assert(m_crowdControl != null);
            Task<bool> result = m_crowdControl.Ping();
            if (waitForPingResponse)
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
    
        /// <summary>Stops and disposes the Crowd Control client instance, if any.</summary>
        void Stop()
        {
            try { m_crowdControl?.Dispose(); }
            catch { /**/ }
            try { m_crowdControl = null; }
            catch { /**/ }
        }
    
        /// <summary>Unity physics update loop; forwards timing to the Crowd Control client for processing.</summary>
        void FixedUpdate()
        {
            m_crowdControl?.Update(Time.time, Time.deltaTime);
        }
    
        /// <summary>Unity callback invoked when the component is destroyed; ensures disposal.</summary>
        void OnDestroy() => Dispose();

        public void ReportCapabilities()
        {
            throw new NotImplementedException();
        }

        public IEffectPack.PrecheckResult Precheck(out string message)
        {
            throw new NotImplementedException();
        }

        public void ProcessRequest(IEffectPackProcessable request)
        {
            throw new NotImplementedException();
        }

        public bool StopAllEffects()
        {
            throw new NotImplementedException();
        }

        public Task<string> InterpolateText(string message)
        {
            throw new NotImplementedException();
        }
    }
}