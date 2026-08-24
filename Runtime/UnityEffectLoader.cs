using CrowdControl.Client.WebSocket.Actions;
using CrowdControl.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling.Memory.Experimental;

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
        /// Guards <see cref="m_loaded"/> and the registry, so that a client loading content on one thread cannot race
        /// a client being disposed on another. Disposal can run on the GC finalizer thread.
        /// </summary>
        private readonly object m_loadLock = new();

        /// <summary>
        /// Unity lifecycle method that initializes the effect registry by scanning child components.
        /// </summary>
        void Start() => LoadEffects();

        /// <summary>
        /// Populates the effect registry from the child components, unless it is already populated.
        /// </summary>
        private void LoadEffects()
        {
            lock (m_loadLock)
            {
                if (m_loaded) return;
                m_loaded = true;
                ScanChildren();
            }
        }

        /// <summary>
        /// Discards the current registry and rescans the child components.
        /// </summary>
        /// <remarks>
        /// Call this after adding or removing <see cref="UnityEffectBase"/> components at runtime. Reconnecting does
        /// not need it: the registry deliberately survives a disconnect, see <see cref="IEffectLoader.Unload"/>.
        /// </remarks>
        public void Reload()
        {
            lock (m_loadLock)
            {
                Effects.Clear();
                m_loaded = true;
                ScanChildren();
            }
        }

        /// <summary>
        /// Registers every effect exposed by the child <see cref="UnityEffectBase"/> components.
        /// </summary>
        private void ScanChildren()
        {
            foreach (UnityEffectBase effect in GetComponentsInChildren<UnityEffectBase>())
            {
                effect.Initialize();
                foreach (string id in ((IEffect)effect).IDs)
                {
                    if (Effects.TryAdd(id, effect))
                    {
                        Log.Debug($"Registered effect: {id}");
                    }
                    else
                    {
                        Log.Error($"Duplicate effect ID detected: {id}. This entry will be ignored.");
                    }
                }
            }
        }

        /// <summary>
        /// Unity lifecycle method that clears the effect registry when the loader is destroyed.
        /// </summary>
        void OnDestroy()
        {
            lock (m_loadLock)
            {
                Effects.Clear();
                m_loaded = false;
            }
        }

        /// <inheritdoc />
        void IEffectLoader.Load() => LoadEffects();

        /// <inheritdoc />
        /// <remarks>
        /// Deliberately does not clear the registry.
        /// <para>
        /// This loader is a scene component shared by every <see cref="WebSocket.CrowdControl"/> instance, yet each
        /// client calls this from its own disposal. Disposal is asynchronous and can also run from a finalizer, so a
        /// client being torn down may unload <em>after</em> its replacement has already loaded. Clearing here left the
        /// replacement reporting <c>ContentLoaded == true</c> against an empty registry while every effect component
        /// was still alive, and every subsequent effect request failed to resolve a handler.
        /// </para>
        /// <para>
        /// The registry is derived purely from the child components, so keeping it populated for as long as this
        /// GameObject lives is always correct. <see cref="OnDestroy"/> still clears it when the object really goes
        /// away, and <see cref="Reload"/> forces an explicit rescan.
        /// </para>
        /// </remarks>
        void IEffectLoader.Unload() { }

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