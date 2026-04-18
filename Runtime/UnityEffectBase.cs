using System;
using System.Collections.Generic;
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
        /// The primary effect ID associated with this effect.
        /// </summary>
        [SerializeField, Tooltip("The effect ID associated with this effect.")]
        public string EffectID = string.Empty;
        string IEffect.EffectID => EffectID;
        IReadOnlyList<string> IEffect.IDs => new[] { EffectID };

        /// <summary>
        /// The display name associated with this effect.
        /// </summary>
        [SerializeField, Tooltip("The display name associated with this effect.")]
        public string Name = string.Empty;

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
        IReadOnlyList<string> IEffect.Conflicts => Conflicts;

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
        SITimeSpan IEffect.DefaultDuration => DefaultDuration;

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
        public bool IsTimed => DefaultDuration > 0;

        /// <summary>
        /// Gets the owning Crowd Control instance.
        /// </summary>
        public WebSocket.CrowdControl CrowdControl => CrowdControlBehavior.CrowdControl;

        /// <summary>
        /// Gets the Crowd Control behavior component that provides access to game state and configuration.
        /// </summary>
        public CrowdControlBehavior CrowdControlBehavior { get; private set; }

        /// <summary>
        /// Indicates whether the effect has been initialized. This is used to prevent multiple initializations in the Unity lifecycle.
        /// </summary>
        [NonSerialized]
        private bool m_initialized;

        /// <summary>
        /// Unity lifecycle method. Initializes the effect when the GameObject is first loaded. This ensures that the effect is ready to handle requests as soon as it becomes active in the scene.
        /// </summary>
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

            CrowdControlBehavior = FindFirstObjectByType<CrowdControlBehavior>();
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