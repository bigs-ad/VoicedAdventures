using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuestVoiceStreaming;

class FakeAudio : IAudioSink
{
    public int Bytes, Starts; public bool Stopped;
    public int BufferedBytes { get { return Bytes; } }
    public int Underruns { get { return 0; } }
    public void Add(byte[] b, int n) { if(n % 2 != 0) throw new Exception("unaligned"); Bytes += n; }
    public void Start() { Starts++; }
    public Task Drain(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(0); }
    public void Stop() { Stopped = true; }
    public void Dispose() { Stop(); }
}
class FakeSocket : WebSocket
{
    public bool Hang;
    public Queue<Tuple<byte[],WebSocketMessageType,bool>> Frames = new Queue<Tuple<byte[],WebSocketMessageType,bool>>();
    public Action BeforeReceive;
    public void Json(string s, bool end = true) { Frames.Enqueue(Tuple.Create(Encoding.UTF8.GetBytes(s),WebSocketMessageType.Text,end)); }
    public void Pcm(int n) { Frames.Enqueue(Tuple.Create(new byte[n],WebSocketMessageType.Binary,true)); }
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken ct) {
        ct.ThrowIfCancellationRequested(); if(BeforeReceive != null) BeforeReceive();
        if(Hang) await Task.Delay(Timeout.Infinite,ct);
        if(Frames.Count == 0) return new WebSocketReceiveResult(0,WebSocketMessageType.Close,true);
        var f=Frames.Dequeue(); Array.Copy(f.Item1,0,b.Array,b.Offset,f.Item1.Length);
        return new WebSocketReceiveResult(f.Item1.Length,f.Item2,f.Item3);
    }
    public override WebSocketCloseStatus? CloseStatus { get { return null; } }
    public override string CloseStatusDescription { get { return null; } }
    public override WebSocketState State { get { return WebSocketState.Open; } }
    public override string SubProtocol { get { return null; } }
    public override void Abort() { }
    public override void Dispose() { }
    public override Task CloseAsync(WebSocketCloseStatus s,string d,CancellationToken c) { return Task.FromResult(0); }
    public override Task CloseOutputAsync(WebSocketCloseStatus s,string d,CancellationToken c) { return Task.FromResult(0); }
    public override Task SendAsync(ArraySegment<byte> b,WebSocketMessageType t,bool e,CancellationToken c) { throw new NotSupportedException(); }
}
class Tests
{
    static int count;
    static void Check(bool b,string name) { if(!b) throw new Exception(name); count++; }
    static void Fail(Action a,string name) { try { a(); } catch(ProbeException) { count++; return; } throw new Exception(name); }
    static void Run(FakeSocket ws, FakeAudio audio) { StreamPump.Run(ws,audio,delegate(string s){},CancellationToken.None).GetAwaiter().GetResult(); }
    static void Main() {
        CredentialTests.Run();
        var recordedOutput=new FakeAudio();
        using(var recording=new RecordingAudioSink(recordedOutput)) {
            recording.Add(new byte[]{1,2,3,4},4);recording.Start();recording.Drain(CancellationToken.None).GetAwaiter().GetResult();
            Check(recording.ToArray().Length==4 && recording.ToArray()[3]==4 && recordedOutput.Bytes==4,"cache tee preserves exact PCM");
        }
        decimal[] rates={0.6m,0.7m,0.8m,1.0m,1.1m,1.2m,1.3m,1.4m,1.5m,2.0m};
        string[] speeds={"-2","-1.5","-1","0","0.5","1","1.33","1.67","2","4"};
        for(int i=0;i<rates.Length;i++) Check(Signing.Request("123","fake-id","fake-key","test",rates[i]).Contains("&Speed="+speeds[i]+"&"),"speed mapping "+rates[i]);
        Check(Signing.Request("123","fake-id","fake-key","test").Contains("&Speed=0&"),"default speed");
        Check(Signing.Request("123","fake-id","fake-key","test",1m,"501002").Contains("&VoiceType=501002&"),"female VoiceType in signed request");
        Fail(()=>Signing.Request("123","fake-id","fake-key","test",1m,"bad"),"reject invalid voice type");
        Fail(()=>Signing.Request("123","fake-id","fake-key","test",0.5m),"reject slow out of range");
        Fail(()=>Signing.Request("123","fake-id","fake-key","test",2.1m),"reject fast out of range");
        var oldCulture=Thread.CurrentThread.CurrentCulture;
        try { Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo("fr-FR");
            Check(Signing.Request("123","fake-id","fake-key","test",1.3m).Contains("&Speed=1.33&"),"invariant speed");
        } finally { Thread.CurrentThread.CurrentCulture=oldCulture; }
        var p = new SortedDictionary<string,string>(StringComparer.Ordinal); p.Add("Text","a&b +中文"); p.Add("Action","test");
        var u=Signing.Build(p,"test-key");
        Check(u.Contains("Text=a%26b%20%2B%E4%B8%AD%E6%96%87"),"query encoding");
        Check(u.StartsWith("wss://tts.cloud.tencent.com/stream_ws?Action=test&"),"ordinal query");
        Check(!u.Contains("test-key"),"secret not in query");
        Check(u.EndsWith("Signature=a2oNr2t9fZ5b8qeOq9kd3cNANcw%3D"),"fixed HMAC fixture");
        var ws=new FakeSocket(); var audio=new FakeAudio();
        ws.Json("{\"code\":",false); ws.Json("0}");
        ws.Pcm(2559); ws.Pcm(1); ws.Pcm(2560); ws.Pcm(2560); ws.Pcm(2560); ws.Json("{\"code\":0,\"final\":1}");
        ws.BeforeReceive=delegate {
            if(audio.Bytes>0 && audio.Bytes<10240) Check(audio.Starts==0,"prebuffer must absorb short delivery gaps before starting");
            if(ws.Frames.Count==1) Check(audio.Starts==1,"starts at 320ms before final");
        };
        Run(ws,audio); Check(audio.Bytes==10240 && audio.Starts==1,"aligned streaming starts once");
        ws=new FakeSocket(); audio=new FakeAudio(); ws.Pcm(2); ws.Json("{\"code\":0,\"final\":1}"); Run(ws,audio); Check(audio.Starts==1,"short stream drains");
        ws=new FakeSocket(); ws.Json("{\"code\":0,\"final\":1}"); Fail(()=>Run(ws,new FakeAudio()),"empty final");
        ws=new FakeSocket(); ws.Json("{\"code\":123,\"message\":\"private-secret\"}");
        try { Run(ws,new FakeAudio()); } catch(ProbeException e) { Check(!e.Message.Contains("private"),"sanitized error"); }
        ws=new FakeSocket(); ws.Pcm(2560); audio=new FakeAudio(); Fail(()=>Run(ws,audio),"truncation"); Check(audio.Stopped,"failure stops audio");
        ws=new FakeSocket(); ws.Pcm(3); ws.Json("{\"code\":0,\"final\":1}"); Fail(()=>Run(ws,new FakeAudio()),"odd final");
        ws=new FakeSocket(); ws.Json("not-json"); Fail(()=>Run(ws,new FakeAudio()),"bad json");
        ws=new FakeSocket(); audio=new FakeAudio(); var cts=new CancellationTokenSource(); cts.Cancel();
        try { StreamPump.Run(ws,audio,delegate(string s){},cts.Token).GetAwaiter().GetResult(); throw new Exception("cancel"); }
        catch(OperationCanceledException) { Check(audio.Stopped,"cancel stops audio"); }
        ws=new FakeSocket(); audio=new FakeAudio(); cts=new CancellationTokenSource(); ws.Pcm(8192);ws.Pcm(2048);
        ws.BeforeReceive=delegate { if(audio.Starts==1) cts.Cancel(); };
        try { StreamPump.Run(ws,audio,delegate(string s){},cts.Token).GetAwaiter().GetResult(); throw new Exception("mid cancel"); }
        catch(OperationCanceledException) { Check(audio.Stopped,"mid-stream cancel stops audio"); }
        ws=new FakeSocket(); audio=new FakeAudio(); cts=new CancellationTokenSource();ws.Pcm(2560);ws.Pcm(2560);
        ws.BeforeReceive=delegate {if(audio.Bytes==2560)cts.Cancel();};
        try {RunCancelledBuffer(ws,audio,cts.Token);throw new Exception("prebuffer cancel");}
        catch(OperationCanceledException){Check(audio.Stopped && audio.Starts==0,"cancel during prebuffer never starts playback");}
        ws=new FakeSocket(); audio=new FakeAudio(); ws.Pcm(2560); ws.Json("{\"code\":0,\"final\":1}"); Run(ws,audio);
        Check(audio.Bytes==2560 && audio.Starts==1,"new request independent after cancellation");
        ws=new FakeSocket(); ws.Hang=true; audio=new FakeAudio();
        try { StreamPump.Run(ws,audio,delegate(string s){},CancellationToken.None,20).GetAwaiter().GetResult(); throw new Exception("timeout"); }
        catch(OperationCanceledException) { Check(audio.Stopped,"receive timeout stops audio"); }
        ws=new FakeSocket(); for(int i=0;i<33;i++) ws.Json(new string('x',4096),false); Fail(()=>Run(ws,new FakeAudio()),"message bound");
        ws=new FakeSocket(); for(int i=0;i<470;i++) ws.Pcm(4096); Fail(()=>Run(ws,new FakeAudio()),"audio duration bound");
        Console.WriteLine("PASS " + count + " streaming assertions (offline, no cloud/audio)");
    }
    static void RunCancelledBuffer(FakeSocket ws,FakeAudio audio,CancellationToken token){
        StreamPump.Run(ws,audio,delegate(string s){},token).GetAwaiter().GetResult();
    }
}
