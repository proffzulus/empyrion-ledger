using System;

namespace TradeLedgerProbeB
{
    // Short-lived API-thread proof, never persisted. A discovered flag/cache alone is insufficient.
    // Exact 5188 SelectNode refreshes this registered marker before its synchronous local event.
    public sealed class LedgerDepositDiscovery
    {
        public bool ConfirmedEvent,Discovered;
        public int Player,ActorPlayer,ResourceId,MarkerId;
        public object World,CurrentWorld,Actor,LocalActor,Resource,MarkerResource,Marker,ResourceMarker,RegisteredMarker;
        public string Scope,CurrentScope;
        public bool Admitted()
        {
            return ConfirmedEvent&&Player>0&&ActorPlayer==Player&&!string.IsNullOrEmpty(Scope)&&Scope==CurrentScope&&
                World!=null&&ReferenceEquals(World,CurrentWorld)&&Actor!=null&&ReferenceEquals(Actor,LocalActor)&&
                Resource!=null&&ReferenceEquals(Resource,MarkerResource)&&Marker!=null&&ReferenceEquals(Marker,ResourceMarker)&&
                ReferenceEquals(Marker,RegisteredMarker)&&Discovered&&ResourceId>=0&&ResourceId==MarkerId;
        }
        public LedgerDepositDetails Read(string body,string localizedSize)
        {
            // Explicit event admission replaces the visible-label permission ONLY here. No getter
            // refresh, precision formula, viewport, resource scan or later cache replay is involved.
            return Admitted()?LedgerDepositDetails.ParseAdmitted(body,localizedSize):null;
        }
    }
}
