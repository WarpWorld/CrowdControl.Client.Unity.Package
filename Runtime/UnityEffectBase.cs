using System;
using System.Linq;
using System.Reflection;
using CrowdControl.Client.WebSocket;
using CrowdControl.Client.WebSocket.Actions;
using CrowdControl.Common;
using UnityEngine;

namespace CrowdControl.Client.Unity
{
    /// <summary>Represents an effect that can be applied to the game.</summary>
    /// <remarks>Effect implementations should inherit from this class.</remarks>
    public abstract class UnityEffectBase : MonoBehaviour, IEffect
    {
        /// <summary>
        /// Metadata describing this effect, including IDs and default duration.
        /// </summary>
        public EffectAttribute EffectAttribute { get; private set; }

        /// <summary>
        /// The display name associated with this effect.
        /// </summary>
        [SerializeField, Tooltip("The display name associated with this effect.")]
        public string Name = string.Empty;

        /// <summary>
        /// The primary effect ID associated with this effect.
        /// </summary>
        [SerializeField, Tooltip("The effect ID associated with this effect.")]
        public string EffectID = string.Empty;

        /// <summary>
        /// A human-readable description of what the effect does.
        /// </summary>
        [SerializeField, TextArea, Tooltip("A description of the effect.")]
        public string Description;

        /// <summary>
        /// All conflicting effect IDs. If any listed effect is running, this effect will be rejected.
        /// </summary>
        [SerializeField, Tooltip("All conflicting effect IDs.")]
        public string[] Conflicts;

        /// <summary>
        /// The moral alignment of the effect.
        /// </summary>
        [SerializeField, Tooltip("The moral alignment of the effect.")]
        public Morality Morality = Morality.Neutral;

        /// <summary>
        /// How orderly or chaotic the result of the effect is.
        /// </summary>
        [SerializeField, Tooltip("How orderly or chaotic the result of the effect is.")]
        public Orderliness Orderliness = Orderliness.Neutral;

        /// <summary>
        /// The default duration of the effect in seconds.
        /// </summary>
        [SerializeField, Tooltip("The default duration of the effect in seconds.")]
        [Range(0, 600)]
        public int DefaultDuration;

        /// <summary>
        /// The default price of the effect in Crowd Control coins.
        /// This is used for menu file generation and has no impact on actual pricing in the service.
        /// </summary>
        [SerializeField, Tooltip("The default price of the effect in Crowd Control coins.")]
        [Min(1)]
        public int DefaultPrice = 1;

        /// <summary>
        /// Gets a value indicating whether the effect is time-based and thus supports ticking.
        /// </summary>
        public bool IsTimed => EffectAttribute.DefaultDuration > 0;

        /// <summary>
        /// Gets the owning Crowd Control instance.
        /// </summary>
        public WebSocket.CrowdControl CrowdControl { get; }

        /// <summary>
        /// Gets the underlying client socket used for communication.
        /// </summary>
        public ClientSocket Client { get; }

        /// <summary>
        /// Indicates whether the effect has been initialized. This is used to prevent multiple initializations in the Unity lifecycle.
        /// </summary>
        [NonSerialized]
        private bool m_initialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="UnityEffectBase"/> class.
        /// </summary>
        /// <param name="crowdControl">The Crowd Control instance.</param>
        /// <param name="client">The client socket used to communicate with the service.</param>
        protected UnityEffectBase(WebSocket.CrowdControl crowdControl, ClientSocket client)
        {
            CrowdControl = crowdControl;
            Client = client;
        }

        /// <summary>
        /// Unity lifecycle method. Validates attributes and constructs the <see cref="EffectAttribute"/> metadata.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the derived class is decorated with <see cref="EffectAttribute"/>.</exception>
        protected virtual void Awake() => Initialize();

        /// <summary>
        /// Initializes the effect by setting up its attributes and ensuring it is not decorated with the EffectAttribute.
        /// </summary>
        /// <remarks>This method should be called before using the effect to ensure it is properly configured. Subsequent calls have no effect if the effect is already initialized.</remarks>
        /// <exception cref="InvalidOperationException">Thrown if the effect class is decorated with the EffectAttribute, which is not allowed for classes inheriting from UnityEffectBase.</exception>
        public void Initialize()
        {
            if (m_initialized) return;
            m_initialized = true;

            if (GetType().GetCustomAttributes<EffectAttribute>(false).Any())
                throw new InvalidOperationException($"Effect classes inheriting from {nameof(UnityEffectBase)} should not be decorated with the {nameof(EffectAttribute)} attribute.");
            EffectAttribute = new(EffectID, DefaultDuration, Conflicts);
        }

        /// <summary>Starts an effect in response to an effect request.</summary>
        /// <param name="request">The effect request to handle.</param>
        /// <returns>An <see cref="EffectStatus"/> indicating the result of the operation.</returns>
        public abstract EffectStatus StartEffect(EffectRequest request);
        EffectStatus IEffect.Start(EffectRequest request) => StartEffect(request);

        /// <inheritdoc cref="StartEffect"/>
        /// <summary>Performs an update tick for a timed effect.</summary>
        public virtual EffectStatus? TickEffect(EffectRequest request) => null;
        EffectStatus? IEffect.Tick(EffectRequest request) => TickEffect(request);

        /// <inheritdoc cref="StartEffect"/>
        /// <summary>Pauses a timed effect.</summary>
        public virtual EffectStatus? PauseEffect(EffectRequest request) => null;
        EffectStatus? IEffect.Pause(EffectRequest request) => PauseEffect(request);

        /// <inheritdoc cref="StartEffect"/>
        /// <summary>Resumes a paused timed effect.</summary>
        public virtual EffectStatus? ResumeEffect(EffectRequest request) => null;
        EffectStatus? IEffect.Resume(EffectRequest request) => ResumeEffect(request);

        /// <inheritdoc cref="StartEffect"/>
        /// <summary>Stops a running timed effect.</summary>
        public virtual EffectStatus? StopEffect(EffectRequest request) => null;
        EffectStatus? IEffect.Stop(EffectRequest request) => StopEffect(request);

        /// <summary>Converts this effect instance into a serializable <see cref="Effect"/> object for menu file generation.</summary>
        public Effect ToEffect()
        {
            Effect result = new Effect(Name, EffectID);

            result.Alignment = (Alignment)Morality + Orderliness;
            result.Duration = DefaultDuration;
            result.Description = Description;

            return result;
        }
    }
}