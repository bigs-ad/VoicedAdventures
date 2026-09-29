using System;
using System.Collections.Concurrent;
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
using VoicedAdventures.Turtle;

static class AutoReceiver {
    public static async Task<int> Run(string[] args) {
        if(args.Length!=6 || (args[0]!="auto-discover" && args[0]!="auto-game" && args[0]!="auto-offline" && args[0]!="auto-timeout" && args[0]!="auto-corrupt" && args[0]!="auto-missing")) return 2;
        Guid guid;int parentPid,pid;long parentTicks,ticks;
        if(!Guid.TryParseExact(args[1],"N",out guid) || !Int32.TryParse(args[2],out parentPid) || !Int64.TryParse(args[3],out parentTicks) ||
            !Int32.TryParse(args[4],out pid) || !Int64.TryParse(args[5],out ticks) || TraceEventSession.IsElevated()!=true) return 2;
        bool discover=args[0]=="auto-discover";
        var parent=new GameTarget {Pid=parentPid,StartTicks=parentTicks};
        using(var parentProcess=parent.Open(false)) {
        var target=discover?GameTarget.Find():new GameTarget {Pid=pid,StartTicks=ticks};
        using(var process=target.Open(discover || args[0]=="auto-game"))
        using(var pipe=new NamedPipeClientStream(".","QuestVoice-Game-"+args[1],PipeDirection.InOut,PipeOptions.Asynchronous)) {
            await pipe.ConnectAsync(5000).ConfigureAwait(false);GameWire.VerifyPeer(pipe,parentPid,false);
            if(discover) {
                if(!File.ReadAllText(Path.Combine(Path.GetDirectoryName(target.Resources),"Main.lua")).Contains("QVR_AUTO_VERSION = 4"))return 2;
                using(var deadline=new CancellationTokenSource(3000))await GameWire.Write(pipe,new GameMessage {Version=2,Kind="target",Session=args[1],Pid=target.Pid,StartTicks=target.StartTicks,Text=target.Executable},deadline.Token).ConfigureAwait(false);
            }
            return await Capture(pipe,discover?"auto-game":args[0],args[1],target,process).ConfigureAwait(false);
        }
        }
    }
    static async Task<int> Capture(NamedPipeClientStream pipe,string mode,string token,GameTarget target,Process process) {
        bool game=mode=="auto-game";int sourcePid=game?target.Pid:Process.GetCurrentProcess().Id;
        bool turtle=game && GameTarget.UsesTurtleTransport(target.Executable);
        string root=game?target.Resources:Path.Combine(AppContext.BaseDirectory,"receiver-fixture"),ext=game?".wav":".qvr";
        var alphabet=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<16;i++)alphabet.Add(Path.Combine(root,"H"+i.ToString("X")+ext),i);
        alphabet.Add(Path.Combine(root,"B"+ext),16);alphabet.Add(Path.Combine(root,"S"+ext),17);
        string name="QuestVoice-Auto-"+token;TraceEventSession session=null;Timer poll=null;
        var gate=new object();int stopping=0;string failure=null;Task stopTask=null,writer=null,disconnect=null,stimulus=null;
        long sequence=0,firstTicks=0,lastSymbol=0,loss=-1;bool clean=false;
        var decoder=new AutoFrameDecoder();var segments=new AutoSegments();int incomplete=0;var clock=Stopwatch.StartNew();
        var files=turtle?new FileTransport(Path.Combine(Path.GetDirectoryName(target.Executable),"imports"),root,token):null;
        Action<string> fail=delegate(string code){Interlocked.CompareExchange(ref failure,code,null);};
        Action stop=delegate {lock(gate){if(stopping!=0)return;Volatile.Write(ref stopping,1);stopTask=Task.Run(delegate{try{lock(gate){if(session!=null)session.Stop();}}catch{fail("stop-failed");}});}};
        Func<string,GameMessage> message=kind=>new GameMessage {Version=2,Session=token,Pid=target.Pid,StartTicks=target.StartTicks,Kind=kind,Sequence=sequence,EventsLost=0};
        using(var monitor=new CancellationTokenSource()) using(var queue=new BlockingCollection<GameMessage>(8)) {
            try {
                if(alphabet.Keys.Any(p=>!File.Exists(p))) throw new InvalidDataException("resources-missing");
                if(TraceEventSession.GetActiveSessionNames().Contains(name))throw new InvalidDataException("session-exists");
                session=new TraceEventSession(name);session.StopOnDispose=true;
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit|KernelTraceEventParser.Keywords.FileIO|KernelTraceEventParser.Keywords.Thread|KernelTraceEventParser.Keywords.Process);
                writer=Task.Run(async delegate {
                    try {
                        foreach(var item in queue.GetConsumingEnumerable()) {
                            if(Volatile.Read(ref stopping)!=0 || failure!=null) continue;
                            using(var deadline=new CancellationTokenSource(3000)) await GameWire.Write(pipe,item,deadline.Token).ConfigureAwait(false);
                        }
                    }catch(IOException){stop();}catch{fail("pipe-write-failed");stop();}
                });
                Action<int,long> consume=(symbol,eventTicks)=> {
                    try {
                        if(symbol==16)firstTicks=eventTicks;
                        Interlocked.Exchange(ref lastSymbol,clock.ElapsedMilliseconds);
                        var frame=decoder.Add(symbol);if(frame==null)return;
                        if(session.Source.EventsLost!=0)throw new InvalidDataException("events-lost");
                        if(game && frame.Error==null && frame.Segment==null && frame.Metadata==null && !frame.Control)throw new InvalidDataException("old-addon-protocol");
                        if(sequence>=Int64.MaxValue-1)throw new InvalidDataException("frame-budget");sequence++;
                        var item=message(frame.Metadata!=null?"metadata":frame.Control?"control":frame.Error==null?"text":"rejected");item.Text=frame.Text;item.Error=frame.Error;
                        if(frame.Metadata!=null){item.DialogueId=frame.Metadata.DialogueId;AutoMetadata.Copy(frame.Metadata,item);Volatile.Write(ref incomplete,1);}
                        if(frame.Segment!=null) {
                            AutoSegments.Copy(frame.Segment,item);
                            bool accepted=segments.Accept(item);Volatile.Write(ref incomplete,segments.Incomplete?1:0);
                            if(!accepted){sequence--;return;}
                        } else if(frame.Metadata==null) {segments.Reset();Volatile.Write(ref incomplete,0);}
                        item.FirstEventUtcTicks=firstTicks;item.DecodeUtcTicks=DateTime.UtcNow.Ticks;
                        if(!queue.TryAdd(item))throw new InvalidDataException("queue-overflow");
                    }catch{fail("frame-invalid-or-loss");stop();}
                };
                session.Source.Kernel.FileIOCreate+=data=> {
                    if(turtle || Volatile.Read(ref stopping)!=0 || data.ProcessID!=sourcePid)return;
                    long eventTicks=data.TimeStamp.ToUniversalTime().Ticks;
                    int symbol;if(alphabet.TryGetValue(data.FileName,out symbol))consume(symbol,eventTicks);
                };
                disconnect=Task.Run(async delegate {
                    try {await pipe.ReadAsync(new byte[1],0,1,monitor.Token).ConfigureAwait(false);stop();}
                    catch(OperationCanceledException){}catch{stop();}
                });
                poll=new Timer(delegate {
                    try {
                        if(process.HasExited){fail("game-exited");stop();}
                        if(!game && clock.Elapsed.TotalSeconds>=(mode=="auto-timeout"?2:900)){fail("receiver-timeout");stop();}
                        if((decoder.Receiving || Volatile.Read(ref incomplete)!=0) && clock.ElapsedMilliseconds-Interlocked.Read(ref lastSymbol)>15000){fail("frame-timeout");stop();}
                        if(Volatile.Read(ref stopping)!=0 || !Monitor.TryEnter(gate))return;
                        try{if(stopping==0){
                            if(files!=null) {
                                long now=DateTime.UtcNow.Ticks;byte[] value=files.Poll(now);
                                if(value!=null){consume(16,now);foreach(byte b in value){consume(b>>4,now);consume(b&15,now);}consume(17,now);}
                            }
                            session.Flush();if(session.Source.EventsLost!=0){fail("events-lost");stop();}
                        }}finally{Monitor.Exit(gate);}
                    }catch{fail("receiver-monitor");stop();}
                },null,50,50);
                using(var deadline=new CancellationTokenSource(3000))await GameWire.Write(pipe,message("ready"),deadline.Token).ConfigureAwait(false);
                if(mode=="auto-offline" || mode=="auto-corrupt" || mode=="auto-missing")stimulus=Task.Run(delegate {
                    Thread.Sleep(200);
                    var texts=new[]{"First automatic dialogue.","Second automatic dialogue.","Second automatic dialogue."};int index=0;
                    foreach(string text in texts) {
                        if(Volatile.Read(ref stopping)!=0)return;
                        using(var f=File.OpenRead(Path.Combine(root,"B"+ext))){f.ReadByte();}
                        Thread.Sleep(15);
                        var part=new GameMessage {Text=text,DialogueId="010000000200000001000000",SegmentIndex=index++,SegmentCount=3,TotalBytes=System.Text.Encoding.UTF8.GetByteCount(String.Concat(texts)),TextChecksum=AutoSegments.Checksum(String.Concat(texts))};
                        var data=AutoSegments.Encode(part);if(mode=="auto-corrupt" && text.StartsWith("Second"))data[data.Length-1]^=1;
                        foreach(byte b in data)for(int shift=4;shift>=0;shift-=4){using(var f=File.OpenRead(Path.Combine(root,"H"+((b>>shift)&15).ToString("X")+ext))){f.ReadByte();}Thread.Sleep(3);}
                        using(var f=File.OpenRead(Path.Combine(root,"S"+ext))){f.ReadByte();}
                        if(mode=="auto-missing")return;
                        Thread.Sleep(150);
                    }
                });
                session.Source.Process();loss=session.Source.EventsLost;
                if(stimulus!=null)await stimulus.ConfigureAwait(false);
            } catch {fail("receiver-error");}
            finally {
                if(poll!=null)poll.DisposeAsync().AsTask().GetAwaiter().GetResult();
                stop();Task pending;lock(gate){pending=stopTask;}if(pending!=null)pending.GetAwaiter().GetResult();
                queue.CompleteAdding();if(writer!=null)writer.GetAwaiter().GetResult();
                try{if(session!=null)session.Dispose();}catch{fail("cleanup-failed");}
                try{clean=!TraceEventSession.GetActiveSessionNames().Contains(name);}catch{clean=false;}
                monitor.Cancel();if(disconnect!=null)disconnect.GetAwaiter().GetResult();
            }
        }
        sequence++;var final=message(failure==null && loss==0 && clean?"done":"failure");final.Clean=clean;final.EventsLost=loss;final.Error=failure;
        int exitCode=!clean?3:final.Kind=="done"?0:4;
        try{using(var deadline=new CancellationTokenSource(3000))await GameWire.Write(pipe,final,deadline.Token).ConfigureAwait(false);}catch{return exitCode==0?1:exitCode;}
        return exitCode;
    }
}
