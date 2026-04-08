using CrowdControl.Client.WebSocket.Actions;
using CrowdControl.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
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
                foreach (string id in ((IEffect)effect).IDs)
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

#if ENABLE_MONO
        /// <summary>
        /// Dynamically loads an assembly containing effect implementations and registers them with the loader.
        /// </summary>
        /// <remarks>
        /// This method is only available when running with Mono support enabled.
        /// This can be used to dynamically load effect assemblies at runtime, allowing for greater flexibility in how effects are defined and organized.
        /// The assembly must contain types that implement the <see cref="IEffect"/> interface in order to be registered with the loader.
        /// Note that loading assemblies at runtime can have security implications and should be done with caution, ensuring that only trusted assemblies are loaded.
        /// This method is not supported in IL2CPP builds, as IL2CPP does not support dynamic assembly loading. In IL2CPP builds, all effects must be included in the build at compile time and discovered through the standard Unity component scanning process.
        /// </remarks>
        /// <param name="assemblyPath">The file system path to the assembly to load. Must be a valid, non-empty string representing the location of a managed assembly file.</param>
        public void LoadAssembly(string assemblyPath)
        {
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAssignableTo(typeof(IEffect)) && !type.IsInterface && !type.IsAbstract)
                {
                    IEffect effect = (IEffect)Activator.CreateInstance(type)!;
                    foreach (string id in effect.IDs)
                    {
                        Effects.Add(id, effect);
                        if (effect is MonoBehaviour behavior)
                        {
                            GameObject g = new GameObject(type.Name);
                            g.transform.SetParent(transform);
                            g.AddComponent(type);
                            if (effect is UnityEffectBase unityEffect)
                                unityEffect.Initialize();
                        }
                    }
                }
            }
        }
#endif
    }
}