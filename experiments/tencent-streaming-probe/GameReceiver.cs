using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using QuestVoiceStreaming;

class GameReceiver {
    static int Main(string[] args) {
        try { return Run(args).GetAwaiter().GetResult(); }
        catch { return 2; }
    }
    static async Task<int> Run(string[] args) {
        if(args.Length==6 && args[0]=="discover-games") {
            Guid discoverySession;int discoveryParent;long discoveryTicks;
            if(!Guid.TryParseExact(args[1],"N",out discoverySession)||!Int32.TryParse(args[2],out discoveryParent)||!Int64.TryParse(args[3],out discoveryTicks)||TraceEventSession.IsElevated()!=true)return 2;
            return await GameDiscovery.Serve(args[1],discoveryParent,discoveryTicks).ConfigureAwait(false);
        }
        if(args.Length>0 && args[0].StartsWith("auto-")) return await AutoReceiver.Run(args).ConfigureAwait(false);
        if(args.Length!=6 || (args[0]!="game" && args[0]!="offline-success" && args[0]!="offline-timeout")) return 2;
        Guid guid; int parentPid,pid; long parentTicks,ticks;
        if(!Guid.TryParseExact(args[1],"N",out guid) || !Int32.TryParse(args[2],out parentPid) || !Int64.TryParse(args[3],out parentTicks) ||
            !Int32.TryParse(args[4],out pid) || !Int64.TryParse(args[5],out ticks)) return 2;
        if(TraceEventSession.IsElevated()!=true) return 2;
        bool game=args[0]=="game";
        var parent=new GameTarget { Pid=parentPid,StartTicks=parentTicks };
        var target=new GameTarget { Pid=pid,StartTicks=ticks };
        using(var parentProcess=parent.Open(false))
        using(var targetProcess=target.Open(game))
        using(var pipe=new NamedPipeClientStream(".","QuestVoice-Game-"+args[1],PipeDirection.InOut,PipeOptions.Asynchronous)) {
            await pipe.ConnectAsync(5000).ConfigureAwait(false);
            GameWire.VerifyPeer(pipe,parentPid,false);
            return await Capture(pipe,args[0],args[1],target,targetProcess).ConfigureAwait(false);
        }
    }
    static async Task<int> Capture(NamedPipeClientStream pipe,string mode,string token,GameTarget target,Process targetProcess) {
        string sessionName="QuestVoice-Game-"+token;
        string root=mode=="game"?target.Resources:Path.Combine(AppContext.BaseDirectory,"receiver-fixture");
        int sourcePid=mode=="game"?target.Pid:Process.GetCurrentProcess().Id;
        string extension=mode=="game"?".wav":".qvr";
        var alphabet=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<16;i++) alphabet.Add(Path.Combine(root,"H"+i.ToString("X")+extension),i);
        var result=new GameMessage { Version=1,Session=token,Pid=target.Pid,StartTicks=target.StartTicks,Kind="failure",EventsLost=-1 };
        TraceEventSession session=null;
        Timer flush=null,watchdog=null;
        var sessionGate=new object();
        int stopping=0;
        string failure=null;
        Task stopTask=null,stimulus=null;
        Action<string> fail=delegate(string value) { Interlocked.CompareExchange(ref failure,value,null); };
        Action stop=delegate {
            lock(sessionGate) {
                if(stopping!=0) return;
                Volatile.Write(ref stopping,1);
                stopTask=Task.Run(delegate {
                    try { lock(sessionGate) { if(session!=null) session.Stop(); } }
                    catch { fail("stop-failed"); }
                });
            }
        };
        var decoder=new FrameSequenceDecoder(1);
        var lifetime=Stopwatch.StartNew();
        using(var monitor=new CancellationTokenSource()) {
            Task disconnect=null;
            try {
                if(alphabet.Keys.Any(p=>!File.Exists(p))) throw new InvalidDataException("resources-missing");
                if(TraceEventSession.GetActiveSessionNames().Contains(sessionName)) throw new InvalidDataException("session-exists");
                session=new TraceEventSession(sessionName); session.StopOnDispose=true;
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.FileIO |
                    KernelTraceEventParser.Keywords.Thread | KernelTraceEventParser.Keywords.Process);
                session.Source.Kernel.FileIOCreate+=data=> {
                    int value;
                    if(data.ProcessID!=sourcePid || !alphabet.TryGetValue(data.FileName,out value)) return;
                    if(result.FirstEventUtcTicks==0) result.FirstEventUtcTicks=data.TimeStamp.ToUniversalTime().Ticks;
                    try {
                        string text=decoder.Add(value,4);
                        if(text==null) return;
                        GameReceipt.ValidateText(text);
                        result.Text=text; result.DecodeUtcTicks=DateTime.UtcNow.Ticks;
                        stop();
                    } catch { fail("frame-invalid"); stop(); }
                };
                disconnect=Task.Run(async delegate {
                    try {
                        var b=new byte[1];
                        await pipe.ReadAsync(b,0,1,monitor.Token).ConfigureAwait(false);
                        fail("parent-disconnected"); stop();
                    } catch(OperationCanceledException) { }
                      catch { fail("parent-disconnected"); stop(); }
                });
                watchdog=new Timer(delegate {
                    try {
                        if(targetProcess.HasExited) { fail("game-exited"); stop(); }
                        if(lifetime.Elapsed.TotalSeconds>=(mode=="offline-timeout"?2:180)) { fail("receiver-timeout"); stop(); }
                    } catch { fail("game-exited"); stop(); }
                },null,100,100);
                flush=new Timer(delegate {
                    if(Volatile.Read(ref stopping)!=0 || !Monitor.TryEnter(sessionGate)) return;
                    try { if(stopping==0) session.Flush(); }
                    catch { fail("flush-failed"); stop(); }
                    finally { Monitor.Exit(sessionGate); }
                },null,50,50);
                using(var write=new CancellationTokenSource(3000)) {
                    await GameWire.Write(pipe,new GameMessage { Version=1,Session=token,Pid=target.Pid,StartTicks=target.StartTicks,Kind="ready" },write.Token).ConfigureAwait(false);
                }
                if(mode=="offline-success") stimulus=Task.Run(delegate {
                    Thread.Sleep(200);
                    foreach(byte b in FrameDecoder.Encode("Offline bridge test.")) for(int shift=4;shift>=0;shift-=4) {
                        if(Volatile.Read(ref stopping)!=0) return;
                        using(var f=File.OpenRead(Path.Combine(root,"H"+((b>>shift)&15).ToString("X")+extension))) { f.ReadByte(); }
                        Thread.Sleep(5);
                    }
                });
                session.Source.Process();
                result.EventsLost=session.Source.EventsLost;
                if(stimulus!=null) await stimulus.ConfigureAwait(false);
            } catch { fail("receiver-error"); }
            finally {
                if(watchdog!=null) watchdog.DisposeAsync().AsTask().GetAwaiter().GetResult();
                if(flush!=null) flush.DisposeAsync().AsTask().GetAwaiter().GetResult();
                stop();
                Task pending; lock(sessionGate) { pending=stopTask; }
                if(pending!=null) pending.GetAwaiter().GetResult();
                try { if(session!=null) session.Dispose(); } catch { fail("cleanup-failed"); }
                try { result.Clean=!TraceEventSession.GetActiveSessionNames().Contains(sessionName); } catch { result.Clean=false; }
                monitor.Cancel();
                if(disconnect!=null) disconnect.GetAwaiter().GetResult();
            }
        }
        bool valid=failure==null && result.Clean && result.EventsLost==0 && result.Text!=null;
        result.Kind=valid?"result":"failure"; result.Error=valid?null:failure??"receiver-invalid";
        if(!valid) result.Text=null;
        try {
            using(var write=new CancellationTokenSource(3000)) await GameWire.Write(pipe,result,write.Token).ConfigureAwait(false);
        } catch { valid=false; }
        return !result.Clean?3:valid?0:1;
    }
}
