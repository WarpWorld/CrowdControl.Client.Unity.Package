using CrowdControl.Client.WebSocket.Actions;
using CrowdControl.Common;

namespace CrowdControl.Client.Unity
{
    public class EffectState
    {
        public IEffect Effect { get; }
        
        public EffectRequest Request { get; }
        
        public EffectResponse Response { get; set; }

        public EffectState(IEffect effect, EffectRequest request, EffectResponse response)
        {
            Effect = effect;
            Request = request;
            Response = response;
        }
    }
}