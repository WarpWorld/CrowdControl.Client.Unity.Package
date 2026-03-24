using CrowdControl.Client.WebSocket;
using UnityEngine;

namespace CrowdControl.Client.Unity
{
    /// <summary>
    /// Base MonoBehaviour for providing the current game state to the Crowd Control client.
    /// </summary>
    /// <remarks>
    /// Derive from this class and implement <see cref="GetGameState"/> to supply
    /// the game's current <see cref="GameState"/> each frame or on demand.
    /// </remarks>
    public abstract class UnityGameStateManager : MonoBehaviour, IGameStateManager
    {
        /// <summary>
        /// Gets the current game state that Crowd Control should use.
        /// </summary>
        /// <returns>The current <see cref="GameState"/>.</returns>
        public abstract GameState GetGameState();
    }
}