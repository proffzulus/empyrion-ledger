using System;

namespace TradeLedgerProbeB
{
    // Release-latched UI handoff; never synthesize or replay a gameplay keypress.
    public sealed class LedgerWindowSwitch
    {
        public int Control {get;private set;}
        public string Scope {get;private set;}
        private long until;private bool closing;
        public static int ControlForKey(int key){return key==282?25:key==283?76:key==284?24:key==285?78:0;}
        public static int KeyForControl(int control){return control==25?282:control==76?283:control==24?284:control==78?285:0;}
        public static bool Switchable(string key)
        {return key=="PDA"||key=="Pda"||key=="TechTree"||key=="Factory"||key=="Blueprints"||key=="BlueprintLibrary"||key=="PrefabLibrary"||key=="Logistics"||key=="Container";}
        public void Cancel(){Control=0;Scope=null;closing=false;until=0;}
        public void Request(int key,string scope,long now)
        {int control=ControlForKey(key);if(control==0)return;Control=control;Scope=scope;until=now+2500;closing=false;}
        // 1 asks to close our modal; 2 invokes the native registered UI action once after fade.
        public int Step(long now,string scope,bool focused,bool typing,bool open,bool present,bool top,bool empty,bool held,bool foreign=false)
        {
            if(Control==0)return 0;
            if(now>until||scope!=Scope||!focused||typing||foreign||present&&!top){Cancel();return 0;}
            if(!closing){if(held)return 0;if(!open||!top){Cancel();return 0;}closing=true;return 1;}
            return !open&&!present&&empty?2:0;
        }
    }
}
