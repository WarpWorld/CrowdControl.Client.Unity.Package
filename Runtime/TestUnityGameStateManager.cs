using CrowdControl.Client.WebSocket;

namespace CrowdControl.Client.Unity
{
    /// <summary>
    /// Simple test implementation of <see cref="UnityGameStateManager"/> that exposes a mutable <see cref="GameState"/>.
    /// </summary>
    public class TestUnityGameStateManager : UnityGameStateManager
    {
        /// <summary>
        /// Gets or sets the current game state value returned by <see cref="GetGameState"/>.
        /// </summary>
        public GameState GameState { get; set; }
    
        /// <summary>
        /// Returns the current <see cref="GameState"/> for testing purposes.
        /// </summary>
        public override GameState GetGameState() => GameState;
    }
}