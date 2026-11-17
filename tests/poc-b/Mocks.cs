using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading.Tasks;
using Eleon.Modding;
using TradeLedgerProbe;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal sealed class InterfaceMock : RealProxy
    {
        internal readonly Dictionary<string, Func<object[], object>> Calls = new Dictionary<string, Func<object[], object>>();
        internal readonly Dictionary<string, Delegate> Events = new Dictionary<string, Delegate>();
        internal int UnexpectedCalls;
        internal InterfaceMock(Type type) : base(type) { }
        internal T As<T>() { return (T)GetTransparentProxy(); }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object result;
                if (call.MethodName == "GetType") result = GetProxiedType();
                else if (call.MethodName.StartsWith("add_") || call.MethodName.StartsWith("remove_"))
                {
                    string key = call.MethodName.Substring(call.MethodName.IndexOf('_') + 1);
                    Delegate previous; Events.TryGetValue(key, out previous);
                    Events[key] = call.MethodName.StartsWith("add_") ? Delegate.Combine(previous, (Delegate)call.Args[0]) : Delegate.Remove(previous, (Delegate)call.Args[0]);
                    result = null;
                }
                else
                {
                    Func<object[], object> handler;
                    if (!Calls.TryGetValue(call.MethodName, out handler))
                    { UnexpectedCalls++; throw new InvalidOperationException("Forbidden mock call: " + call.MethodName); }
                    result = handler(call.Args);
                }
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }
            catch (Exception error) { return new ReturnMessage(error, call); }
        }
        internal void Emit(string name, params object[] args)
        { Delegate callback; if (Events.TryGetValue(name, out callback) && callback != null) callback.DynamicInvoke(args); }
    }

    internal sealed class Poison
    {
        public override string ToString() { throw new Exception("Must never stringify payload"); }
    }


}
