using CrowdControl.Client.WebSocket.Metadata;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrowdControl.Client.Unity
{
    public class UnityMetadataLoader : MonoBehaviour, IMetadataLoader
    {
        [NonSerialized]
        private bool m_loaded;

        /// <summary>
        /// Gets a mapping of metadata IDs to their corresponding values.
        /// </summary>
        public IDictionary<string, IMetadata> Metadata { get; } = new Dictionary<string, IMetadata>();

        /// <summary>
        /// Unity lifecycle method that initializes the metadata registry by scanning child components.
        /// </summary>
        void Start()
        {
            if (m_loaded) return;
            m_loaded = true;

            foreach (UnityMetadataBase metadata in GetComponentsInChildren<UnityMetadataBase>())
                Metadata.Add(metadata.Key, metadata);
        }

        /// <summary>
        /// Unity lifecycle method that clears the metadata registry when the loader is destroyed.
        /// </summary>
        void OnDestroy()
        {
            Metadata.Clear();
            m_loaded = false;
        }

        /// <inheritdoc />
        void IMetadataLoader.Load() => Start();

        /// <inheritdoc />
        void IMetadataLoader.Unload() => OnDestroy();
    }
}