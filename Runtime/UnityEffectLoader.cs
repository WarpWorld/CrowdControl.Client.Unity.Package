using CrowdControl.Client.WebSocket.Actions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace CrowdControl.Client.Unity
{
    /// <summary>
    /// Discovers and registers Unity-based effect components for use by Crowd Control.
    /// </summary>
    public class UnityEffectLoader : MonoBehaviour, IEffectLoader
    {
        [NonSerialized]
        private bool m_loaded;
    
        /// <summary>
        /// Gets a mapping of effect IDs to their corresponding <see cref="IEffect"/> handlers.
        /// </summary>
        public IDictionary<string, IEffect> Effects { get; } = new ConcurrentDictionary<string, IEffect>();

        private CrowdControlBehavior? m_crowdControl;

        void Awake() => m_crowdControl = FindFirstObjectByType<CrowdControlBehavior>();

        /// <summary>
        /// Unity lifecycle method that initializes the effect registry by scanning child components.
        /// </summary>
        void Start()
        {
            if (m_loaded) return;
            m_loaded = true;

            foreach (UnityEffectBase effect in GetComponentsInChildren<UnityEffectBase>())
            {
                effect.Initialize();
                foreach (string id in effect.EffectAttribute.IDs)
                    Effects.Add(id, effect);
            }
        }

        /// <summary>
        /// Unity lifecycle method that clears the effect registry when the loader is destroyed.
        /// </summary>
        void OnDestroy()
        {
            Effects.Clear();
            m_loaded = false;
        }

        /// <inheritdoc />
        void IEffectLoader.Load() => Start();
    
        /// <inheritdoc />
        void IEffectLoader.Unload() => OnDestroy();
    }
}