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

        public abstract bool TryGetSerialized(out JToken? value);

        public abstract event Action Updated;
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

        private protected override object? GetUntypedValue() => Value!;

        TValue IMetadata<TValue>.Value => Value!;
    }
}