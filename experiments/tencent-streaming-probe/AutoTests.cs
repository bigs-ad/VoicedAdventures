using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QuestVoiceStreaming;

class AutoTests {
    static int checks;
    static void Check(bool yes,string name) { if(!yes) throw new Exception("FAIL "+name); checks++; }
    static void Reject(Action fn,string name) { try { fn(); } catch(FormatException) { checks++;return; } catch(InvalidDataException) { checks++;return; } throw new Exception("FAIL "+name); }
    static AutoFrame Feed(AutoFrameDecoder d,string text,int kind=0,bool end=true) {
        var bytes=AutoFrameDecoder.Encode(text,kind);d.Add(16);
        foreach(byte b in bytes) { Check(d.Add(b>>4)==null,"no early delivery");Check(d.Add(b&15)==null,"no early delivery"); }
        return end?d.Add(17):null;
    }
    static GameMessage Msg(string kind,long seq) { return new GameMessage {Version=2,Session="auto-test",Pid=7,StartTicks=9,Kind=kind,Sequence=seq,Text="text",EventsLost=0,FirstEventUtcTicks=DateTime.UtcNow.AddSeconds(-1).Ticks,DecodeUtcTicks=DateTime.UtcNow.Ticks}; }
    static AutoRequest Request(string text) { return new AutoRequest { Message=new GameMessage {Text=text},Rate=1.2m }; }
    static GameMessage Part(string id,string text,int index,int count,string whole) {
        return new GameMessage {DialogueId=id.PadLeft(24,'0'),Text=text,SegmentIndex=index,SegmentCount=count,TotalBytes=System.Text.Encoding.UTF8.GetByteCount(whole),TextChecksum=AutoSegments.Checksum(whole)};
    }
    static AutoFrame Decode(byte[] data) {
        var decoder=new AutoFrameDecoder();decoder.Add(16);foreach(byte b in data){decoder.Add(b>>4);decoder.Add(b&15);}return decoder.Add(17);
    }
    static async Task LongTests() {
        string whole="First sentence.\n"+new string('\u4e2d',100)+"\ud83d\ude00";
        var a=Part("1","First sentence.\n",0,2,whole);var b=Part("1",whole.Substring(a.Text.Length),1,2,whole);
        var assembly=new AutoSegments();Check(!assembly.Accept(b),"ignore mid dialogue attachment");
        a=Decode(AutoSegments.Encode(a)).Segment;b=Decode(AutoSegments.Encode(b)).Segment;
        Check(assembly.Accept(a) && assembly.Incomplete,"first segment before whole");
        Check(assembly.Accept(b) && !assembly.Incomplete,"compact final assembly valid");
        Reject(()=>assembly.Accept(b),"duplicate final segment");
        assembly=new AutoSegments();assembly.Accept(a);Reject(()=>assembly.Accept(a),"duplicate first while incomplete");
        assembly=new AutoSegments();assembly.Accept(a);var wrong=Part("2",b.Text,1,2,whole);Reject(()=>assembly.Accept(wrong),"mixed dialogue refused");
        assembly=new AutoSegments();assembly.Accept(a);wrong=Part("1",b.Text,1,3,whole);Reject(()=>assembly.Accept(wrong),"changed count refused");
        assembly=new AutoSegments();assembly.Accept(a);wrong=Part("1","corruption",1,2,whole);Reject(()=>assembly.Accept(wrong),"whole checksum and length");
        wrong=Part("1",new string('x',236),0,1,new string('x',236));Reject(()=>AutoSegments.Validate(wrong),"payload metadata space reserved");
        wrong=Part("1","\ud800",0,1,"x");Reject(()=>AutoSegments.Validate(wrong),"unpaired surrogate");
        var budget=new NarrationBudget();var calls=new List<string>();var gates=new Queue<TaskCompletionSource<bool>>();
        var first=new TaskCompletionSource<bool>();var second=new TaskCompletionSource<bool>();gates.Enqueue(first);gates.Enqueue(second);
        CancellationToken firstToken=CancellationToken.None;
        var c=new AutoNarration(budget,async (r,ct)=>{calls.Add(r.Message.Text);Check(r.Rate==1.2m,"whole dialogue rate snapshot");firstToken=ct;await gates.Dequeue().Task.WaitAsync(ct);},delegate{});
        c.Enable(true,true);c.Accept(new AutoRequest {Message=a,Rate=1.2m});
        Check(calls.Count==1,"starts before final segment arrives");
        c.Accept(new AutoRequest {Message=b,Rate=2m});Check(!firstToken.IsCancellationRequested && calls.Count==1,"continuation queues without interruption");
        first.SetResult(true);second.SetResult(true);await c.Work;Check(calls.Count==2,"ordered full narration");
        c.Accept(new AutoRequest {Message=a,Rate=1.2m});c.Accept(new AutoRequest {Message=b,Rate=1.2m});await c.Work;Check(calls.Count==2,"whole duplicate skipped");
        var blocked=new TaskCompletionSource<bool>();calls.Clear();CancellationToken token=CancellationToken.None;
        c=new AutoNarration(new NarrationBudget(),async(r,ct)=>{calls.Add(r.Message.Text);token=ct;await blocked.Task.WaitAsync(ct);},delegate{});
        c.Enable(true,true);c.Accept(new AutoRequest {Message=a,Rate=1});c.Accept(new AutoRequest {Message=b,Rate=1});c.Disable();
        Check(!token.IsCancellationRequested,"off preserves playing segment");blocked.SetResult(true);await c.Work;Check(calls.Count==1,"off drops pending segments");
        calls.Clear();var states=new List<string>();var failGate=new TaskCompletionSource<bool>();
        c=new AutoNarration(new NarrationBudget(),async(r,ct)=>{calls.Add(r.Message.Text);await failGate.Task;throw new OperationCanceledException();},s=>states.Add(s));
        c.Enable(true,true);c.Accept(new AutoRequest {Message=a,Rate=1});c.Accept(new AutoRequest {Message=b,Rate=1});failGate.SetResult(true);await c.Work;
        Check(!c.Enabled && calls.Count==1 && states.Contains("speech-failed"),"timed out segment stops queued cloud requests");
        calls.Clear();var release=new TaskCompletionSource<bool>();CancellationToken oldToken=CancellationToken.None;
        c=new AutoNarration(new NarrationBudget(),async(r,ct)=>{calls.Add(r.Message.Text);if(r.Message.DialogueId==a.DialogueId){oldToken=ct;try{await Task.Delay(Timeout.Infinite,ct);}catch(OperationCanceledException){}await release.Task;ct.ThrowIfCancellationRequested();}},delegate{});
        c.Enable(true,true);c.Accept(new AutoRequest {Message=a,Rate=1});c.Accept(new AutoRequest {Message=b,Rate=1});
        c.Accept(new AutoRequest {Message=Part("3","New NPC",0,1,"New NPC"),Rate=1});
        c.Accept(new AutoRequest {Message=Part("4","Latest NPC",0,1,"Latest NPC"),Rate=1});
        release.SetResult(true);await c.Work;Check(oldToken.IsCancellationRequested && calls.Count==2 && calls[1]=="Latest NPC","replacement cancels old audio and drops old tail and intermediate NPC");
        calls.Clear();c=new AutoNarration(new NarrationBudget(),(r,ct)=>{calls.Add(r.Message.Text);return Task.CompletedTask;},delegate{});c.Enable(true,true);
        for(int i=0;i<8;i++){c.Accept(new AutoRequest {Message=Part("5","same",i,8,"samesamesamesamesamesamesamesame"),Rate=1});await c.Work;}
        Check(calls.Count==8 && c.Enabled,"eight segments including identical sentences play normally");
    }
    static void MetadataTests() {
        var meta=Msg("metadata",1);meta.DialogueId="010000000200000001000000";
        meta.SpeakerName="阿尔萨斯😀";meta.NpcId="123";meta.SpeakerRace="人类";meta.SpeakerSex="male";meta.Source="npc";meta.QuestTitle="任务";
        var decoded=Decode(AutoMetadata.Encode(meta));Check(decoded.Metadata.SpeakerName==meta.SpeakerName && decoded.Segment==null,"Unicode metadata not narration");
        var r=new AutoReceipt("auto-test",7,9);r.Accept(Msg("ready",0));Check(r.Accept(meta)==null,"metadata callback suppressed");
        var first=Msg("text",2);AutoSegments.Copy(Part(meta.DialogueId,"one",0,2,"onetwo"),first);first.Text="one";
        Check(r.Accept(first).SpeakerName==meta.SpeakerName,"first segment annotated before final");
        var last=Msg("text",3);AutoSegments.Copy(Part(meta.DialogueId,"two",1,2,"onetwo"),last);last.Text="two";
        Check(r.Accept(last).NpcId=="123","continuation annotated");
        var missing=Msg("text",4);AutoSegments.Copy(Part("2","new",0,1,"new"),missing);missing.Text="new";missing.SpeakerName="spoof";
        Check(r.Accept(missing).SpeakerName=="" && missing.Source=="","missing metadata unknown and not stale/spoofed");
        r=new AutoReceipt("auto-test",7,9);r.Accept(Msg("ready",0));r.Accept(meta);missing.Sequence=2;
        Reject(()=>r.Accept(missing),"metadata exact dialogue identity required");
        var payload=AutoMetadata.Payload(meta);Reject(()=>AutoMetadata.Decode(new byte[17]),"short metadata");
        var trailing=new byte[payload.Length+1];Array.Copy(payload,trailing,payload.Length);Reject(()=>AutoMetadata.Decode(trailing),"trailing metadata");
        payload[13]=255;Reject(()=>AutoMetadata.Decode(payload),"invalid metadata UTF8");
        meta.Source="target";Reject(()=>AutoMetadata.Payload(meta),"source allowlist");meta.Source="questlog";
        Check(AutoMetadata.Decode(AutoMetadata.Payload(meta)).SpeakerName==meta.SpeakerName,"quest log accepts observed giver metadata");meta.Source="npc";meta.SpeakerName=new string('x',65);
        Reject(()=>AutoMetadata.Payload(meta),"bounded speaker");meta.SpeakerName="bad\nname";Reject(()=>AutoMetadata.Payload(meta),"metadata controls");
        meta.SpeakerName="NPC";meta.MetadataVersion=5;meta.GameRealm="Classic Beta PvE";meta.GameBuild="70009";meta.PlayerName="Alice";
        var scoped=Decode(AutoMetadata.Encode(meta)).Metadata;
        Check(scoped.MetadataVersion==5 && scoped.GameRealm==meta.GameRealm && scoped.GameBuild=="70009" && scoped.PlayerName=="Alice","v5 scope roundtrip");
        var json=System.Text.Json.JsonSerializer.Serialize(scoped);
        var wire=System.Text.Json.JsonSerializer.Deserialize<GameMessage>(json,new System.Text.Json.JsonSerializerOptions {UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});
        Check(wire.PlayerName=="Alice" && wire.MetadataVersion==5,"strict JSON retains v5 scope");
        r=new AutoReceipt("auto-test",7,9);r.Accept(Msg("ready",0));r.Accept(meta);
        first=Msg("text",2);AutoSegments.Copy(Part(meta.DialogueId,"one",0,2,"onetwo"),first);first.Text="one";
        last=Msg("text",3);AutoSegments.Copy(Part(meta.DialogueId,"two",1,2,"onetwo"),last);last.Text="two";
        Check(r.Accept(first).GameRealm==meta.GameRealm && r.Accept(last).PlayerName=="Alice","scope retained across segments");
        var copy=new GameMessage();AutoMetadata.Copy(null,copy);Check(copy.MetadataVersion==0 && copy.GameRealm=="" && copy.PlayerName=="","scope reset without metadata");
        meta.GameRealm=new string('x',97);Reject(()=>AutoMetadata.Payload(meta),"realm byte bound");meta.GameRealm=new string('x',96);meta.PlayerName=new string('x',72);meta.QuestTitle=new string('x',96);
        Reject(()=>AutoMetadata.Payload(meta),"v5 aggregate 255 byte bound without truncation");
        meta.GameRealm="Classic Beta PvE";meta.PlayerName="Alice";meta.QuestTitle="quest";meta.GameBuild="70009x";Reject(()=>AutoMetadata.Payload(meta),"scope build digits only");meta.GameBuild="70009";
        meta.MetadataVersion=4;Reject(()=>AutoMetadata.Payload(meta),"v4 cannot smuggle scope");
        meta.MetadataVersion=6;meta.QuestId="91743";meta.QuestLevel=12;meta.QuestStage="detail";
        meta.RaceSource="unit";meta.SexSource="unit";meta.SpeakerKind="npc";meta.QuestLevelSource="quest-log";
        var extended=Decode(AutoMetadata.Encode(meta)).Metadata;
        Check(extended.QuestId=="91743" && extended.QuestLevel==12 && extended.QuestStage=="detail" && extended.RaceSource=="unit" && extended.SexSource=="unit" && extended.SpeakerKind=="npc" && extended.QuestLevelSource=="quest-log","v6 quest identity roundtrip");
        meta.IdentityConflict=true;extended=Decode(AutoMetadata.Encode(meta)).Metadata;
        Check(extended.IdentityConflict,"v6 conflict retained");
        r=new AutoReceipt("auto-test",7,9);r.Accept(Msg("ready",0));r.Accept(meta);
        first=Msg("text",2);AutoSegments.Copy(Part(meta.DialogueId,"one",0,2,"onetwo"),first);first.Text="one";
        last=Msg("text",3);AutoSegments.Copy(Part(meta.DialogueId,"two",1,2,"onetwo"),last);last.Text="two";
        Check(r.Accept(first).QuestId=="91743" && r.Accept(last).IdentityConflict,"v6 metadata across all segments");
        AutoMetadata.Copy(null,extended);Check(extended.QuestId=="" && extended.QuestLevel==null && extended.RaceSource=="" && !extended.IdentityConflict && extended.SpeakerKind=="","v6 no stale metadata");
        foreach(string invalid in new[]{"0","-1","01","+1","2147483648","1x"}){meta.QuestId=invalid;Reject(()=>AutoMetadata.Payload(meta),"invalid quest ID "+invalid);}meta.QuestId="91743";
        meta.QuestLevel=-2;Reject(()=>AutoMetadata.Payload(meta),"invalid negative quest level");meta.QuestLevel=-1;
        Check(Decode(AutoMetadata.Encode(meta)).Metadata.QuestLevel==-1,"scaling level not guessed");meta.QuestLevel=null;
        Check(Decode(AutoMetadata.Encode(meta)).Metadata.QuestLevel==null,"missing level not zero");
        meta.RaceSource="guess";Reject(()=>AutoMetadata.Payload(meta),"invalid provenance");meta.RaceSource="unit";
        meta.QuestStage="acceptance";Reject(()=>AutoMetadata.Payload(meta),"invalid quest stage");meta.QuestStage="detail";
        meta.MetadataVersion=5;Reject(()=>AutoMetadata.Payload(meta),"v5 cannot smuggle provenance");
    }
    static async Task Run() {
        MetadataTests();
        var longReceipt=new AutoReceipt("auto-test",7,9);longReceipt.Accept(Msg("ready",0));
        for(int i=1;i<=9000;i++){var command=Msg("control",i);command.Text="stop";longReceipt.Accept(command);}
        Check(true,"long session exceeds former frame budget");
        foreach(string scenario in new[]{"clean","loss","unclean","wrong-order","other-error"}) {
            var exitReceipt=new AutoReceipt("auto-test",7,9);exitReceipt.Accept(Msg("ready",0));
            var ending=Msg("failure",scenario=="wrong-order"?2:1);ending.Clean=scenario!="unclean";ending.EventsLost=scenario=="loss"?1:0;ending.Error=scenario=="other-error"?"frame-timeout":"game-exited";
            try {exitReceipt.Accept(ending);throw new Exception("missing failure");}
            catch(InvalidDataException e){Check((e.Message=="game-exited-clean")== (scenario=="clean"),"only validated safe game exit can reconnect");}
        }
        Check(Feed(new AutoFrameDecoder(),"stop",3).Text=="stop","game stop command");
        Check(Feed(new AutoFrameDecoder(),"settings:system:10",3).Text=="settings:system:10","local provider command");
        Check(Feed(new AutoFrameDecoder(),"settings:tencent:20",3).Control,"control is not narration");
        Check(GameControl.Parse("settings:system:10:queue").QueueMode,"game FIFO setting");
        Check(GameControl.Parse("settings:system:10:queue:0").Volume==0,"game volume mute");
        Check(GameControl.Parse("settings:system:10:interrupt:200").Volume==200,"game volume boost");
        Check(GameControl.Parse("settings:system:10").Volume==null,"old addon preserves assistant volume");
        foreach(string volume in new[]{"-1","201","01","+1","1.5","","abc"})Reject(()=>GameControl.Parse("settings:system:10:queue:"+volume),"invalid volume rejected");
        Check(!GameControl.Parse("settings:tencent:12:interrupt").QueueMode,"game interrupt setting");
        Reject(()=>GameControl.Parse("settings:system:10:other"),"unknown queue mode");
        foreach(string command in new[]{"settings:other:10","settings:system:5","settings:system:21","settings:system:010","settings:system:+10","STOP","stop:extra","settings:system:10\n"})
            Reject(()=>GameControl.Parse(command),"strict command allowlist");
        var controlReceipt=new AutoReceipt("auto-test",7,9);controlReceipt.Accept(Msg("ready",0));
        var start=Msg("text",1);AutoSegments.Copy(Part("8","one",0,2,"onetwo"),start);start.Text="one";
        controlReceipt.Accept(start);
        var stopCommand=Msg("control",2);stopCommand.Text="stop";Check(controlReceipt.Accept(stopCommand)==stopCommand,"stop resets partial dialogue");
        var done=Msg("done",3);done.Clean=true;Check(controlReceipt.Accept(done)==null,"clean stop can end before tail");
        var providerCalls=new List<string>();
        var providerNarrator=new AutoNarration(new NarrationBudget(),(r,ct)=>{providerCalls.Add(r.Provider);return Task.CompletedTask;},delegate{});
        providerNarrator.Enable(true,true);
        providerNarrator.Accept(new AutoRequest {Message=Part("9","one",0,2,"onetwo"),Rate=1,Provider="system"});await providerNarrator.Work;
        providerNarrator.Accept(new AutoRequest {Message=Part("9","two",1,2,"onetwo"),Rate=2,Provider="tencent"});await providerNarrator.Work;
        Check(providerCalls.Count==2 && providerCalls[1]=="system","provider snapshot prevents mid-dialogue cloud switch");
        await LongTests();
        var staleGate=new TaskCompletionSource<bool>();var staleCalls=new List<string>();var staleStates=new List<string>();
        var stale=new AutoNarration(new NarrationBudget(),async(r,ct)=>{staleCalls.Add(r.Message.Text);if(r.Message.Text=="old"){await staleGate.Task;throw new InvalidOperationException("late socket failure");}},s=>staleStates.Add(s));
        stale.Enable(true,true);stale.Accept(Request("old"));stale.Accept(Request("new"));staleGate.SetResult(true);await stale.Work;
        Check(stale.Enabled && staleCalls.Count==2 && !staleStates.Contains("speech-failed"),"cancelled old failure cannot disable replacement");
        var segment=new byte[]{81,65,2,21,1,0,0,0,2,0,0,0,1,0,0,0,0,1,1,0,121,0,121,0,120,0,0};
        int ca=0,cb=0;for(int i=0;i<segment.Length-2;i++){ca=(ca+segment[i])%255;cb=(cb+ca)%255;}
        segment[segment.Length-2]=(byte)ca;segment[segment.Length-1]=(byte)cb;
        var segmentedDecoder=new AutoFrameDecoder();segmentedDecoder.Add(16);
        foreach(byte b in segment){segmentedDecoder.Add(b>>4);segmentedDecoder.Add(b&15);}
        Check(segmentedDecoder.Add(17).Text=="x","decode versioned segment");
        var d=new AutoFrameDecoder();Check(d.Add(1)==null && d.Add(17)==null,"ignore mid frame");
        var first=Feed(d,"first");Check(first!=null && first.Text=="first","first bounded frame");
        Check(Feed(d,"second").Text=="second","consecutive frame");
        Check(Feed(d,"\u4f60\u597d\ud83d\ude00").Text=="\u4f60\u597d\ud83d\ude00","compact Chinese and surrogate pair");
        Feed(d,"unfinished",0,false);Check(Feed(d,"fresh").Text=="fresh","new start resets partial");
        Check(Feed(d,"too-long",1).Error=="too-long","rejection not narration");
        d.Add(16);d.Add(5);Reject(()=>d.Add(17),"odd or truncated frame");
        d=new AutoFrameDecoder();var bytes=AutoFrameDecoder.Encode("checksum",0);bytes[bytes.Length-1]^=1;d.Add(16);
        foreach(byte b in bytes){d.Add(b>>4);d.Add(b&15);}Reject(()=>d.Add(17),"bad checksum");
        d=new AutoFrameDecoder();Feed(d,"extra",0,false);Reject(()=>{d.Add(0);d.Add(0);},"extra symbols");
        var receipt=new AutoReceipt("auto-test",7,9);Reject(()=>receipt.Accept(Msg("text",1)),"unarmed frame");
        receipt=new AutoReceipt("auto-test",7,9);receipt.Accept(Msg("ready",0));Check(receipt.Accept(Msg("text",1)).Text=="text","receipt");
        Reject(()=>receipt.Accept(Msg("text",1)),"replay");
        receipt=new AutoReceipt("auto-test",7,9);receipt.Accept(Msg("ready",0));var lost=Msg("text",1);lost.EventsLost=1;Reject(()=>receipt.Accept(lost),"loss invalidates");
        receipt=new AutoReceipt("auto-test",7,9);receipt.Accept(Msg("ready",0));var wrong=Msg("text",1);wrong.Session="old";Reject(()=>receipt.Accept(wrong),"wrong auto session");
        receipt=new AutoReceipt("auto-test",7,9);receipt.Accept(Msg("ready",0));wrong=Msg("text",1);wrong.Pid=8;Reject(()=>receipt.Accept(wrong),"wrong auto pid");
        receipt=new AutoReceipt("auto-test",7,9);receipt.Accept(Msg("ready",0));wrong=Msg("done",1);wrong.Clean=false;Reject(()=>receipt.Accept(wrong),"automatic cleanup failure");
        var budget=new NarrationBudget();var calls=new List<string>();
        var gate=new TaskCompletionSource<bool>();bool canceled=false;
        var controller=new AutoNarration(budget,async delegate(AutoRequest r,CancellationToken ct) {
            calls.Add(r.Message.Text);Check(r.Rate==1.2m,"rate snapshot");
            if(r.Message.Text=="A") { try { await Task.Delay(Timeout.Infinite,ct); } catch(OperationCanceledException) { canceled=true; } await gate.Task;ct.ThrowIfCancellationRequested(); }
        },delegate {});
        controller.Accept(Request("disabled"));Check(calls.Count==0,"disabled no cloud");
        Check(!controller.Enable(false,true),"consent required");Check(!controller.Enable(true,false),"credentials required");
        Check(controller.Enable(true,true),"enable");
        controller.Accept(Request("A"));controller.Accept(Request("A"));Check(calls.Count==1,"duplicate active no charge");
        controller.Accept(Request("B"));controller.Accept(Request("C"));gate.SetResult(true);await controller.Work;
        Check(canceled && calls.Count==2 && calls[1]=="C","latest pending only");
        controller.Accept(Request("A"));await controller.Work;Check(calls.Count==2,"dedupe across reopen");
        controller.Disable();controller.Accept(Request("off"));Check(calls.Count==2,"off ignores text");
        var held=new TaskCompletionSource<bool>();CancellationToken playing=CancellationToken.None;
        var second=new AutoNarration(budget,async delegate(AutoRequest r,CancellationToken ct){playing=ct;await held.Task.WaitAsync(ct);},delegate {});
        second.Enable(true,true);second.Accept(Request("continues"));second.Disable();Check(!playing.IsCancellationRequested,"toggle off keeps current audio");
        second.Stop();await second.Work;Check(playing.IsCancellationRequested,"Stop cancels audio");
        var capped=new AutoNarration(budget,delegate(AutoRequest r,CancellationToken ct){calls.Add(r.Message.Text);return Task.CompletedTask;},delegate {});
        capped.Enable(true,true);capped.Accept(Request("four"));await capped.Work;capped.Accept(Request("five"));await capped.Work;
        capped.Accept(Request("six"));await capped.Work;Check(budget.Count==6 && capped.Enabled && calls.Contains("six"),"no artificial five-call cap");
        Console.WriteLine("PASS automatic protocol/controller: "+checks+" assertions, no cloud");
    }
    static void LuaFixture(string path) {
        var lines=File.ReadAllLines(path);var assembled=new System.Text.StringBuilder();var sequence=new AutoSegments();
        Func<string,byte[]> hex=s=>{var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(s.Substring(2*i,2),16);return b;};
        string expected=new System.Text.UTF8Encoding(false,true).GetString(hex(lines[0]));
        int count=0;bool metadata=false;
        for(int i=1;i<lines.Length;i++){
            if(lines[i].StartsWith("V")){var m=Decode(hex(lines[i].Substring(1))).Metadata;Check(m.MetadataVersion==5 && m.GameRealm=="Classic Beta PvE" && m.GameBuild=="70009" && m.PlayerName=="Alice","Lua v5 scope decoded in C#");continue;}
            if(lines[i].StartsWith("E")){var m=Decode(hex(lines[i].Substring(1))).Metadata;Check(m.MetadataVersion==6 && m.QuestId=="91743" && m.QuestLevel==12 && m.QuestStage=="detail" && m.RaceSource=="unit" && m.SexSource=="unit" && m.IdentityConflict && m.SpeakerKind=="npc" && m.QuestLevelSource=="quest-log" && m.Source=="npc-m","Lua v6 evidence decoded in C#");continue;}
            if(lines[i].StartsWith("M")){var m=Decode(hex(lines[i].Substring(1))).Metadata;Check(m.SpeakerName=="中文 NPC" && m.NpcId=="456" && m.SpeakerRace=="人类" && m.SpeakerSex=="female" && m.Source=="npc","Lua metadata decoded in C#");metadata=true;continue;}
            var part=Decode(hex(lines[i])).Segment;Check(sequence.Accept(part),"Lua fixture frame accepted");assembled.Append(part.Text);count++;
        }
        Check(metadata,"cross-language metadata fixture present");
        Check(!sequence.Incomplete && assembled.ToString()==expected,"actual Lua to C# lossless Chinese/emoji text and checksum");
        Console.WriteLine("PASS Lua-to-C# fixture: "+count+" segments and Unicode metadata, "+System.Text.Encoding.UTF8.GetByteCount(expected)+" UTF-8 bytes");
    }
    static int Main(string[] args) { try {Run().GetAwaiter().GetResult();if(args.Length==1)LuaFixture(args[0]);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;} }
}
