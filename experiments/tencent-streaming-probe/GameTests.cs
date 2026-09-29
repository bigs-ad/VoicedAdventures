using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuestVoiceStreaming;

class GameTests {
    static int count;
    static void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name); count++; }
    static void Reject(Action action,string name) {
        try { action(); } catch(InvalidDataException) { count++; return; }
        throw new Exception("FAIL: "+name);
    }
    static async Task RejectAsync(Func<Task> action,string name) {
        try { await action(); } catch(InvalidDataException) { count++; return; }
        throw new Exception("FAIL: "+name);
    }
    static GameMessage Msg(string kind) {
        return new GameMessage { Version=1,Session="0123456789abcdef0123456789abcdef",Kind=kind,Pid=42,StartTicks=123,
            Text=kind=="result"?"Live NPC text":null,FirstEventUtcTicks=DateTime.UtcNow.AddSeconds(-1).Ticks,
            DecodeUtcTicks=DateTime.UtcNow.Ticks,EventsLost=0,Clean=true };
    }
    static GameReceipt Receipt() { return new GameReceipt(Msg("ready").Session,42,123); }
    static GameReceipt Ready() { var r=Receipt(); r.Accept(Msg("ready")); return r; }
    static async Task Run() {
        Reject(()=>Receipt().Accept(Msg("result")),"result before ready");
        var r=Ready(); Check(r.Accept(Msg("result")).Text=="Live NPC text","real text returned");
        Reject(()=>r.Accept(Msg("result")),"duplicate result");
        Reject(()=>Ready().Accept(Msg("ready")),"duplicate ready");
        var bad=Msg("ready"); bad.Session="old-session"; Reject(()=>Receipt().Accept(bad),"wrong session");
        bad=Msg("ready"); bad.Pid=43; Reject(()=>Receipt().Accept(bad),"wrong game pid");
        bad=Msg("ready"); bad.StartTicks++; Reject(()=>Receipt().Accept(bad),"pid reuse");
        bad=Msg("ready"); bad.Version=2; Reject(()=>Receipt().Accept(bad),"unknown version");
        bad=Msg("result"); bad.EventsLost=1; Reject(()=>Ready().Accept(bad),"ETW loss");
        bad=Msg("result"); bad.Clean=false; Reject(()=>Ready().Accept(bad),"cleanup failure");
        bad=Msg("result"); bad.Text=" "; Reject(()=>Ready().Accept(bad),"empty dialogue");
        bad=Msg("result"); bad.Text=new string('\u4f60',128); Reject(()=>Ready().Accept(bad),"encoded limit");
        bad=Msg("result"); bad.Text="a\0b"; Reject(()=>Ready().Accept(bad),"control text");
        bad=Msg("result"); bad.Text="\ud800"; Reject(()=>Ready().Accept(bad),"unpaired surrogate");
        bad=Msg("result"); bad.FirstEventUtcTicks=0; Reject(()=>Ready().Accept(bad),"missing timestamp");
        bad=Msg("result"); bad.DecodeUtcTicks=bad.FirstEventUtcTicks-1; Reject(()=>Ready().Accept(bad),"reversed timestamps");
        using(var memory=new MemoryStream()) {
            await GameWire.Write(memory,Msg("result"),CancellationToken.None);
            byte[] bytes=memory.ToArray();
            using(var fragmented=new Fragments(bytes)) {
                var read=await GameWire.Read(fragmented,CancellationToken.None);
                Check(read.Text=="Live NPC text","fragmented packet");
                Check(await GameWire.Read(fragmented,CancellationToken.None)==null,"clean EOF");
            }
            using(var shortPacket=new MemoryStream(bytes,0,bytes.Length-1))
                await RejectAsync(async()=>{ await GameWire.Read(shortPacket,CancellationToken.None); },"truncated payload");
        }
        using(var oversized=new MemoryStream(BitConverter.GetBytes(8193)))
            await RejectAsync(async()=>{ await GameWire.Read(oversized,CancellationToken.None); },"oversize before allocation");
        using(var shortHeader=new MemoryStream(new byte[]{1,2}))
            await RejectAsync(async()=>{ await GameWire.Read(shortHeader,CancellationToken.None); },"truncated header");
        using(var invalidJson=new MemoryStream(new byte[]{1,0,0,0,(byte)'!'}))
            await RejectAsync(async()=>{ await GameWire.Read(invalidJson,CancellationToken.None); },"malformed JSON");
        using(var canceled=new CancellationTokenSource()) using(var memory=new MemoryStream()) {
            canceled.Cancel(); bool stopped=false;
            try { await GameWire.Read(memory,canceled.Token); } catch(OperationCanceledException) { stopped=true; }
            Check(stopped,"cancel before receive");
        }
        string pipeName="QuestVoice-Test-"+Guid.NewGuid().ToString("N");
        using(var server=GameWire.CreateServer(pipeName))
        using(var client=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous)) {
            var accept=server.WaitForConnectionAsync(); await client.ConnectAsync(1000); await accept;
            GameWire.VerifyPeer(server,Process.GetCurrentProcess().Id,true); count++;
            Reject(()=>GameWire.VerifyPeer(server,Process.GetCurrentProcess().Id+1,true),"wrong helper process");
            GameWire.VerifyPeer(client,Process.GetCurrentProcess().Id,false); count++;
            await GameWire.Write(client,Msg("ready"),CancellationToken.None);
            Check((await GameWire.Read(server,CancellationToken.None)).Kind=="ready","real pipe roundtrip");
            using(var timeout=new CancellationTokenSource(50)) {
                bool stopped=false;
                try { await GameWire.Read(server,timeout.Token); } catch(OperationCanceledException) { stopped=true; }
                Check(stopped,"idle read timeout");
            }
            client.Dispose(); Check(await GameWire.Read(server,CancellationToken.None)==null,"parent disconnect EOF");
        }
        Console.WriteLine("PASS game bridge: "+count+" checks (no cloud, no real credentials)");
    }
    static int Main() { try { Run().GetAwaiter().GetResult(); return 0; } catch(Exception e) { Console.Error.WriteLine(e); return 1; } }
    sealed class Fragments : MemoryStream {
        public Fragments(byte[] data):base(data) { }
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct) { return base.ReadAsync(buffer,offset,Math.Min(count,1),ct); }
    }
}
