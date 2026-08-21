using CrowdControl.Client.WebSocket.Metadata;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;

namespace CrowdControl.Client.Unity
{
    /// <summary>
    /// Serves as an abstract base class for metadata components in Unity, providing a unique key and supporting untyped
    /// value retrieval and serialization through the IMetadata interface.
    /// </summary>
    /// <remarks>Derived classes must implement the Key property to supply a unique identifier for the
    /// metadata instance. The class also defines mechanisms for retrieving and serializing metadata values, and exposes
    /// an event to notify listeners when the metadata is updated. This base class is intended to be used as a component
    /// within Unity scenes to associate metadata with GameObjects.</remarks>
    public abstract class UnityMetadataBase : MonoBehaviour, IMetadata
    {
        /// <summary>The primary ID associated with this metadata.</summary>
        [Tooltip("The primary ID associated with this metadata.")]
        public abstract string Key { get; }

        // Do NOT expose a public Value here; only through the interface.
        object? IMetadata.Value => GetUntypedValue();

        /// <summary>
        /// Gets the untyped value for the IMetadata interface.
        /// </summary>
        private protected abstract object? GetUntypedValue();

        /// <inheritdoc/>
        public abstract bool TryGetSerialized(out JToken? value);

        /// <inheritdoc/>
        public event Action? Updated;

        /// <summary>
        /// Raises the Updated event to notify listeners that the metadata has been updated. This method can be called by derived classes when the metadata value changes, allowing external components to react to updates.
        /// </summary>
        protected virtual void OnUpdated()
        {
            CrowdControlBehavior?.CrowdControl?.UpdateMetadata(Key, GetUntypedValue());
            Updated?.Invoke();
        }

        /// <summary>
        /// Gets the Crowd Control behavior component that provides access to game state and configuration.
        /// </summary>
        public CrowdControlBehavior CrowdControlBehavior { get; private set; }

        /// <summary>
        /// Unity lifecycle method. Initializes the effect when the GameObject is first loaded. This ensures that the effect is ready to handle requests as soon as it becomes active in the scene.
        /// </summary>
        protected virtual void Awake() => CrowdControlBehavior = FindFirstObjectByType<CrowdControlBehavior>();

        /// <summary>
        /// Initializes the metadata object by setting up any necessary state or configuration.
        /// </summary>
        /// <remarks>This method should be called before using the metadata object to ensure it is properly configured.</remarks>
        public virtual void Initialize() { }
    }

    /// <summary>
    /// Serves as an abstract base class for strongly-typed metadata, providing a contract for accessing a value of a
    /// specified type.
    /// </summary>
    /// <remarks>Derived classes must implement the Value property to supply the metadata value. This class
    /// enables type-safe access to metadata values and is intended to be extended for concrete metadata
    /// scenarios.</remarks>
    /// <typeparam name="TValue">The type of the value associated with the metadata. The specific meaning and usage of this value are defined by
    /// derived implementations.</typeparam>
    public abstract class UnityMetadataBase<TValue> : UnityMetadataBase, IMetadata<TValue>
    {
        /// <summary>The value associated with this metadata. The type and meaning of this value is determined by the specific metadata implementation.</summary>
        [Tooltip("The value associated with this metadata. The type and meaning of this value is determined by the specific metadata implementation.")]
        public abstract TValue Value { get; }

        private protected override object? GetUntypedValue() => Value;

        TValue IMetadata<TValue>.Value => Value;

        /// <summary>Occurs when the metadata value is updated.</summary>
        public new event Action<TValue>? Updated;

        /// <summary>
        /// Raises the Updated event to notify listeners that the metadata has been updated, passing the new value as an argument. This method can be called by derived classes when the metadata value changes, allowing external components to react to updates with knowledge of the new value.
        /// </summary>
        protected override void OnUpdated()
        {
            base.OnUpdated();
            Updated?.Invoke(Value);
        }
    }
}