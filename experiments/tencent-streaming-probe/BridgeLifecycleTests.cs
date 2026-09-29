using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using QuestVoiceStreaming;

class BridgeLifecycleTests {
    static void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name); }
    static async Task Run(string mode) {
        if(mode=="--live-ready" || mode=="--live-dual-ready") {
            int required=mode=="--live-dual-ready"?2:1,count=0;bool ready=false;
            using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
                try{await GameCapture.Listen(cancel.Token,state=>{Console.WriteLine(state);if(state=="ready" && Interlocked.Increment(ref count)>=required){ready=true;cancel.Cancel();}},delegate(GameMessage m){if(m.Kind=="client-added")Console.WriteLine("Client registered: "+m.Pid);});}
                catch(OperationCanceledException){if(!ready)throw;}
            }
            Check(ready,"live receiver ready without cloud or narration");
            Console.WriteLine("PASS live game discovery, authenticated pipe, receiver ready and cleanup");return;
        }
        if(mode=="--auto") {
            int count=0;
            using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(40))) {
                bool stopped=false;
                try {await GameCapture.Listen(cancel.Token,Console.WriteLine,delegate(GameMessage m){count++;Check(m.Sequence==count && m.Kind=="text","automatic sequence");Check(m.SegmentIndex==count-1 && m.SegmentCount==3 && m.DialogueId=="010000000200000001000000","segmented metadata survives real ETW and IPC");Check(m.Text==(count==1?"First automatic dialogue.":"Second automatic dialogue."),"automatic text");if(count==3)cancel.Cancel();},"auto-offline");}
                catch(OperationCanceledException){stopped=true;}
                Check(stopped && count==3,"continuous frames then clean disconnect");
            }
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
                bool failed=false;try{await GameCapture.Listen(timeout.Token,Console.WriteLine,delegate {throw new Exception("unexpected frame");},"auto-timeout");}
                catch(InvalidDataException){failed=true;}Check(failed,"automatic deadline");
            }
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
                bool failed=false;int delivered=0;
                try{await GameCapture.Listen(timeout.Token,Console.WriteLine,delegate{delivered++;},"auto-corrupt");}
                catch(InvalidDataException){failed=true;}
                Check(failed && delivered==1,"late corrupt frame invalidates after first delivery");
            }
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35))) {
                bool failed=false;int delivered=0;
                try{await GameCapture.Listen(timeout.Token,Console.WriteLine,delegate{delivered++;},"auto-missing");}
                catch(InvalidDataException){failed=true;}
                Check(failed && delivered==1,"missing continuation times out and invalidates partial dialogue");
            }
            Console.WriteLine("PASS real automatic ETW multi-frame, sequence, parent disconnect, deadline and cleanup");return;
        }
        if(mode=="--offline") {
            int launched=0,cleaned=0,removed=0,scans=0;
            using(var cancel=new CancellationTokenSource(5000)) {
                var a=new GameTarget {Pid=10,StartTicks=100};var b=new GameTarget {Pid=20,StartTicks=200};
                try {
                    await GameCapture.Supervise(cancel.Token,m=>{if(m.Kind=="client-removed")removed++;},delegate {
                        scans++;
                        if(scans>=4)cancel.Cancel();
                        return scans==1?new List<GameTarget>{a}:new List<GameTarget>{a,b};
                    },async delegate(GameTarget target,CancellationToken token){
                        launched++;
                        try {
                            if(target.Pid==10){await Task.Delay(100,token);throw new InvalidDataException("game-exited-clean");}
                            await Task.Delay(Timeout.Infinite,token);return null;
                        }finally{Interlocked.Increment(ref cleaned);}
                    });
                }catch(OperationCanceledException){}
                Check(launched==2 && cleaned==2 && removed==1,"hot add, independent clean game exit, no restart of retired client, cleanup all receivers");
            }
            cleaned=0;
            using(var cancel=new CancellationTokenSource(5000)) {
                bool failed=false;
                try {
                    await GameCapture.Supervise(cancel.Token,delegate{},()=>new List<GameTarget>{new GameTarget{Pid=1,StartTicks=1},new GameTarget{Pid=2,StartTicks=2}},async delegate(GameTarget target,CancellationToken token){
                        try {if(target.Pid==1)throw new InvalidDataException("frame-invalid");await Task.Delay(Timeout.Infinite,token);return null;}
                        finally {Interlocked.Increment(ref cleaned);}
                    });
                }catch(InvalidDataException e){failed=e.Message=="frame-invalid";}
                Check(failed && cleaned==2,"integrity failure closes both receivers without retry");
            }
            var discovered=GameTarget.FromDiscovery(new GameMessage {Version=2,Kind="target",Session="test",Pid=123,StartTicks=456,Text=@"E:\Game\_classic_beta_\WowB.exe"},"test");
            Check(discovered.Pid==123 && discovered.StartTicks==456,"discovery identity preserved");
            bool badDiscovery=false;
            try{GameTarget.FromDiscovery(new GameMessage {Version=2,Kind="target",Session="wrong",Pid=123,StartTicks=456,Text=@"E:\Game\_classic_beta_\WowB.exe"},"test");}catch(InvalidDataException){badDiscovery=true;}
            Check(badDiscovery,"cross-session discovery rejected");
            Check(GameTarget.ResourcesFor(@"E:\玩家游戏\World of Warcraft\_classic_beta_\WowB.exe")==@"E:\玩家游戏\World of Warcraft\_classic_beta_\Interface\AddOns\VoicedAdventures\Sounds","portable Chinese path");
            Check(GameTarget.ResourcesFor(@"E:\玩家游戏\World of Warcraft\_retail_\Wow.exe")==@"E:\玩家游戏\World of Warcraft\_retail_\Interface\AddOns\VoicedAdventures\Sounds","retail portable path");
            Check(!GameTarget.UsesTurtleTransport(@"E:\Game\_retail_\Wow.exe"),"retail must not use Turtle imports transport");
            Check(!GameTarget.UsesTurtleTransport(@"E:\Game\_classic_beta_\WowB.exe"),"beta uses modern transport");
            Check(GameTarget.UsesTurtleTransport(@"E:\Game\twmoa_1181\WoW.exe"),"Turtle keeps file transport");
            Check(GameTarget.ClientLabel(@"E:\Game\_retail_\Wow.exe")=="正式服","retail label isolates Turtle identity fallback");
            Check(GameTarget.ClientLabel(@"E:\Game\_classic_beta_\WowB.exe")=="无限服","beta label preserved");
            foreach(var path in new[]{@"WowB.exe",@"E:\Game\_classic_beta_\Other.exe",@"E:\Game\_retail_\WowB.exe"}) {
                bool invalid=false;try{GameTarget.ResourcesFor(path);}catch(InvalidDataException){invalid=true;}
                Check(invalid,"unsupported game path rejected");
            }
            var game=new GameTarget { Pid=Process.GetCurrentProcess().Id,StartTicks=1 };
            bool refused=false;
            try { using(game.Open(false)) { } } catch(InvalidDataException) { refused=true; }
            Check(refused,"process start mismatch refused");
            using(var cancel=new CancellationTokenSource()) {
                cancel.Cancel(); refused=false;
                try { await GameCapture.Receive(cancel.Token,delegate {},"offline-success"); } catch(OperationCanceledException) { refused=true; }
                Check(refused,"pre-cancel must not launch receiver");
            }
            Console.WriteLine("PASS lifecycle preflight, no UAC or cloud"); return;
        }
        if(mode!="--elevated") throw new Exception("Use --offline or --elevated");
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90))) {
            var text=await GameCapture.Receive(timeout.Token,Console.WriteLine,"offline-success");
            Check(text.Text=="Offline bridge test." && text.Clean && text.EventsLost==0,"offline ETW text, loss and cleanup");
            Console.WriteLine("PASS offline ETW decode and cleanup");
        }
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90))) {
            bool refused=false;
            try { await GameCapture.Receive(timeout.Token,Console.WriteLine,"offline-timeout"); }
            catch(InvalidDataException) { refused=true; }
            Check(refused,"receiver deadline must refuse text"); Console.WriteLine("PASS offline receiver timeout");
        }
        using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(90))) {
            bool stopped=false;
            try { await GameCapture.Receive(cancel.Token,delegate(string state) { Console.WriteLine(state); if(state=="ready") cancel.Cancel(); },"offline-success"); }
            catch(OperationCanceledException) { stopped=true; }
            Check(stopped,"stop while listening"); Console.WriteLine("PASS parent disconnect and owned-session cleanup");
        }
        using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(90))) {
            bool stopped=false;
            try { await GameCapture.Receive(cancel.Token,delegate(string state) { if(state=="validated") cancel.Cancel(); },"offline-success"); }
            catch(OperationCanceledException) { stopped=true; }
            Check(stopped,"stop after decoded frame must prevent delivery"); Console.WriteLine("PASS cancellation after decode prevents delivery");
        }
    }
    static int Main(string[] args) { try { Run(args.Length==1?args[0]:"--offline").GetAwaiter().GetResult(); return 0; } catch(Exception e) { Console.Error.WriteLine(e); return 1; } }
}
