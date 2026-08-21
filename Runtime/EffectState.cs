using CrowdControl.Client.WebSocket.Actions;
using CrowdControl.Common;

namespace CrowdControl.Client.Unity
{
    /// <summary>Represents the state and response associated with a processed effect request.</summary>
    public class EffectState
    {
        /// <summary>Gets the effect that processed the request.</summary>
        public IEffect Effect { get; }
        
        /// <summary>Gets the effect request being processed.</summary>
        public EffectRequest Request { get; }
        
        /// <summary>Gets or sets the response produced for the request.</summary>
        public EffectResponse Response { get; set; }

        /// <summary>Initializes a new effect state instance.</summary>
        /// <param name="effect">The effect that processed the request.</param>
        /// <param name="request">The request being processed.</param>
        /// <param name="response">The response produced for the request.</param>
        public EffectState(IEffect effect, EffectRequest request, EffectResponse response)
        {
            Effect = effect;
            Request = request;
            Response = response;
        }
    }
}