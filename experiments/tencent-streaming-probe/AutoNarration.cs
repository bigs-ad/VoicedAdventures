using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace QuestVoiceStreaming {
    public sealed class NarrationBudget { public long Count {get;private set;} public bool Take(){Count++;return true;} }
    public sealed class VoiceSnapshot {
        public string Identity {get;private set;}
        public string Label {get;private set;}
        public string Status {get;private set;}
        public bool IsQwen {get{return Identity=="qwen:Ethan" || Identity=="qwen:Cherry";}}
        public string QwenModel {get{return IsQwen?"qwen3-tts-flash-realtime-2025-11-27":null;}}
        public string CloudVoice {get{return IsQwen?Identity.Substring(5):Identity;}}
        public VoiceSnapshot(string identity,string label,string status){Identity=identity;Label=label;Status=status;}
    }
    public static class SpeakerVoiceRouting {
        static readonly List<KeyValuePair<string,string>> categories=BuildCategories();
        public static IList<KeyValuePair<string,string>> Categories {get{return categories.AsReadOnly();}}
        static List<KeyValuePair<string,string>> BuildCategories(){
            var result=new List<KeyValuePair<string,string>>();
            foreach(string race in new[]{"人类","矮人","暗夜精灵","侏儒","兽人","亡灵","牛头人","巨魔","天裔","高等精灵"}){
                result.Add(new KeyValuePair<string,string>("category:"+race+":male",race+" · 男"));
                result.Add(new KeyValuePair<string,string>("category:"+race+":female",race+" · 女"));
            }
            result.Add(new KeyValuePair<string,string>("reserved:child:male","男童（预留）"));
            result.Add(new KeyValuePair<string,string>("reserved:child:female","女童（预留）"));
            result.Add(new KeyValuePair<string,string>("reserved:neutral","无性别角色（预留）"));
            result.Add(new KeyValuePair<string,string>("default","未知角色 · 默认声音"));
            return result;
        }
        public static VoiceSnapshot Resolve(string provider,GameMessage message,IDictionary<string,string> mappings=null,string[] installed=null){
            if(provider!="system" && provider!="tencent" && provider!="qwen")throw new ArgumentException("Unsupported provider.");
            string sex=message==null?null:message.SpeakerSex,race=message==null?null:message.SpeakerRace,npc=message==null?null:message.NpcId;
            string chosen=null,level="默认回退";
            bool book=AutoMetadata.BaseSource(message)=="book";
            if(book){
                sex=null;race=null;npc=null;level="书籍旁白";
                if(mappings!=null && !mappings.TryGetValue(provider+":reserved:neutral",out chosen))mappings.TryGetValue(provider+":sex:male",out chosen);
                if(chosen=="@none")chosen=null;
            }
            foreach(string key in new[]{"npc:"+npc,"category:"+race+":"+sex,"sex:"+sex}){
                if(book)break;
                if(key.StartsWith("npc:") && String.IsNullOrEmpty(npc))continue;
                if(key.StartsWith("category:") && (String.IsNullOrEmpty(race) || String.IsNullOrEmpty(sex)))continue;
                string value;
                if(mappings!=null && mappings.TryGetValue(provider+":"+key,out value) && !String.IsNullOrWhiteSpace(value) && value!="@none"){chosen=value;level=key.StartsWith("npc:")?"明确配置":key.StartsWith("category:")?"类别配置":key.StartsWith("sex:")?"通用性别配置":"默认回退";break;}
            }
            if(provider=="tencent" || provider=="qwen"){
                if(chosen==null && sex!="male" && sex!="female" && mappings!=null){
                    string defaultVoice;if(mappings.TryGetValue(provider+":default",out defaultVoice) && !String.IsNullOrWhiteSpace(defaultVoice) && defaultVoice!="@none")chosen=defaultVoice;
                }
                if(chosen=="@general:male" || chosen=="@general:female"){
                    string followSex=chosen.Substring(9),resolved=null;
                    if(mappings!=null)mappings.TryGetValue(provider+":sex:"+followSex,out resolved);
                    if(resolved!=null && resolved.StartsWith("@",StringComparison.Ordinal))throw new ArgumentException("General voice must be a concrete voice.");
                    chosen=String.IsNullOrWhiteSpace(resolved)?(provider=="tencent"?(followSex=="female"?"501002":"601008"):(followSex=="female"?"qwen:Cherry":"qwen:Ethan")):resolved;
                    level=followSex=="female"?"跟随通用女声":"跟随通用男声";
                }
                if(provider=="qwen"){
                    if(chosen==null && mappings!=null){string fallbackVoice;if(mappings.TryGetValue("qwen:sex:male",out fallbackVoice)){chosen=fallbackVoice;level="暂用千问通用男声";}}
                    if(chosen==null)chosen=sex=="female"?"qwen:Cherry":"qwen:Ethan";
                    if(!(chosen=="qwen:Cherry"||chosen=="qwen:Ethan"))throw new ArgumentException("Invalid Qwen voice ID.");
                    string female,male,undeadFemale,undeadMale,dwarfMale,label=chosen=="qwen:Cherry"?"Cherry（女声）":"Ethan（男声）";
                    if(mappings!=null && mappings.TryGetValue("qwen:category:矮人:male",out dwarfMale) && chosen==dwarfMale)label="矮人男";
                    else if(mappings!=null && mappings.TryGetValue("qwen:category:亡灵:female",out undeadFemale) && chosen==undeadFemale)label="亡灵女";
                    else if(mappings!=null && mappings.TryGetValue("qwen:category:亡灵:male",out undeadMale) && chosen==undeadMale)label="亡灵男";
                    foreach(var role in Categories){
                        string roleVoice;if(mappings!=null && mappings.TryGetValue("qwen:"+role.Key,out roleVoice) && chosen==roleVoice && role.Key!="default")label=role.Key=="reserved:neutral"?"书籍旁白":role.Value.Replace(" · ","").Replace("（预留）","");
                    }
                    if(mappings!=null && mappings.TryGetValue("qwen:sex:female",out female) && chosen==female)label="千问 · 通用女声";
                    else if(mappings!=null && mappings.TryGetValue("qwen:sex:male",out male) && chosen==male)label="千问 · 通用男声";
                    return new VoiceSnapshot(chosen,label,level+" · 阿里云北京");
                }
                if(chosen==null){chosen=sex=="female"?"501002":"601008";level=sex=="male"||sex=="female"?"通用性别音色":"性别未知，默认回退";}
                int number;if(!Int32.TryParse(chosen,out number)||number<=0)throw new ArgumentException("Invalid Tencent VoiceType.");
                return new VoiceSnapshot(chosen,chosen=="601008"?"爱小豪 (601008)":chosen=="501002"?"智菊 (501002)":chosen,level);
            }
            // The host inventory contains only enabled Chinese voices: name, gender, culture.
            if(chosen==null && mappings!=null){string defaultVoice;if(mappings.TryGetValue("system:default",out defaultVoice) && !String.IsNullOrWhiteSpace(defaultVoice))chosen=defaultVoice;}
            string[] fallback=null,matching=null,configured=null;
            foreach(string line in installed??new string[0]){
                string[] item=line.Split('\t');if(item.Length!=3 || String.IsNullOrWhiteSpace(item[0]))continue;
                if(fallback==null || item[2]=="zh-CN")fallback=item;
                if(item[1]==sex && (matching==null || item[2]=="zh-CN"))matching=item;
                if(item[0]==chosen)configured=item;
            }
            if(configured!=null)return new VoiceSnapshot(configured[0],configured[0],level);
            if(matching!=null)return new VoiceSnapshot(matching[0],matching[0],chosen==null?"本机对应性别声音":"配置声音不可用，回退本机对应性别声音");
            return new VoiceSnapshot(fallback==null?"":fallback[0],fallback==null?"无可用中文声音":fallback[0],fallback==null?"未安装中文声音，不调用云端":"缺少对应声音，回退本机中文默认声音");
        }
    }
    public sealed class AutoRequest { public GameMessage Message; public decimal Rate; public string Provider="system"; public VoiceSnapshot Voice; }
    public static class SpeechText {
        public static List<string> Split(string text,int maximum){
            if(maximum<2)throw new ArgumentOutOfRangeException("maximum");
            var result=new List<string>();int start=0;
            while(start<text.Length){
                int end=Math.Min(start+maximum,text.Length);
                bool naturalBoundary=false;
                int minimum=result.Count==0?24:120;
                for(int i=start;i<end;i++){
                    if(i-start+1<minimum || "。！？!?；;\n".IndexOf(text[i])<0)continue;
                    int boundary=i+1;
                    while(boundary<end && "。！？!?；;\r\n\"'”’」』）)".IndexOf(text[boundary])>=0)boundary++;
                    end=boundary;naturalBoundary=true;break;
                }
                if(end<text.Length && !naturalBoundary){
                    int boundary=0;
                    for(int i=start;i<end;i++)if("。！？!?\n".IndexOf(text[i])>=0)boundary=i+1;
                    if(boundary==0)for(int i=start;i<end;i++)if("，,；;：: \t".IndexOf(text[i])>=0)boundary=i+1;
                    if(boundary>start)end=boundary;
                    if(Char.IsHighSurrogate(text[end-1]))end--;
                }
                result.Add(text.Substring(start,end-start));start=end;
            }
            return result;
        }
    }
    public sealed class NarrationEntry {
        public DateTimeOffset ReceivedAt {get;private set;}
        public NarrationEntry(){ReceivedAt=DateTimeOffset.Now;}
        public string Id {get;internal set;}
        public string Text {get;internal set;}
        public string Provider {get;internal set;}
        public decimal Rate {get;internal set;}
        public VoiceSnapshot Voice {get;internal set;}
        public string State {get;internal set;}
        public bool Complete {get;internal set;}
        public GameMessage Message {get;internal set;}
        public readonly List<AutoRequest> Parts=new List<AutoRequest>();
        internal int Played;
        internal List<AutoRequest> SpeechParts;
        internal bool Candidate;
        internal bool ProtectedChain,Manual;
        internal readonly AutoSegments Assembly=new AutoSegments();
    }
    public sealed class AutoNarration {
        public bool Enabled {get;private set;}
        public bool QueueMode {get;set;}
        public event Action Changed;
        public Func<AutoRequest,VoiceSnapshot> ResolveVoice {get;set;}
        readonly NarrationBudget budget;readonly Func<AutoRequest,CancellationToken,Task> speak;readonly Action<string> state;
        readonly HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
        readonly Queue<string> seenOrder=new Queue<string>();
        readonly List<NarrationEntry> entries=new List<NarrationEntry>();
        readonly List<NarrationEntry> pending=new List<NarrationEntry>();
        readonly List<NarrationEntry> candidates=new List<NarrationEntry>();
        sealed class Recent {public NarrationEntry Entry;public DateTime Finished;}
        readonly List<Recent> recent=new List<Recent>();
        public Func<DateTime> UtcNow=()=>DateTime.UtcNow;
        static bool SameSpeaker(GameMessage a,GameMessage b){return a.Pid==b.Pid && a.StartTicks==b.StartTicks && a.Session==b.Session && a.SpeakerName==b.SpeakerName && a.NpcId==b.NpcId && a.SpeakerRace==b.SpeakerRace && a.SpeakerSex==b.SpeakerSex;}
        void PruneRecent(){var now=UtcNow();recent.RemoveAll(r=>now-r.Finished>=TimeSpan.FromMinutes(2));}
        bool RecentlyCompleted(GameMessage message,string text,VoiceSnapshot voice,string provider,decimal rate){PruneRecent();return recent.Exists(r=>SameSpeaker(r.Entry.Message,message) && r.Entry.Text==text && r.Entry.Voice.Identity==voice.Identity && r.Entry.Provider==provider && r.Entry.Rate==rate);}
        bool PossibleRecent(GameMessage message){PruneRecent();return recent.Exists(r=>SameSpeaker(r.Entry.Message,message) && System.Text.Encoding.UTF8.GetByteCount(r.Entry.Text)==message.TotalBytes && AutoSegments.Checksum(r.Entry.Text)==message.TextChecksum);}
        bool Protect(GameMessage message){return AutoMetadata.TurnIn(message) || (!AutoMetadata.Manual(message) && AutoMetadata.BaseSource(message)=="npc" && pending.Exists(e=>Active(e) && e.ProtectedChain && e.Message.Pid==message.Pid && e.Message.StartTicks==message.StartTicks && e.Message.Session==message.Session));}
        static bool SameNpc(GameMessage a,GameMessage b){
            if(a.Pid!=b.Pid || a.StartTicks!=b.StartTicks || a.Session!=b.Session)return false;
            if(!String.IsNullOrEmpty(a.NpcId) && !String.IsNullOrEmpty(b.NpcId))return a.NpcId==b.NpcId;
            return !String.IsNullOrWhiteSpace(a.SpeakerName) && a.SpeakerName==b.SpeakerName;
        }
        bool SameNpcTaskPending(GameMessage message){
            if(AutoMetadata.Manual(message) || AutoMetadata.BaseSource(message)!="npc" || String.IsNullOrWhiteSpace(message.QuestTitle))return false;
            return pending.Exists(e=>Active(e) && AutoMetadata.BaseSource(e.Message)=="npc" && !String.IsNullOrWhiteSpace(e.Message.QuestTitle) && SameNpc(e.Message,message));
        }
        public IList<NarrationEntry> Entries {get{return entries.AsReadOnly();}}
        CancellationTokenSource current;NarrationEntry playing;bool pumping;Task work=Task.CompletedTask;
        public Task Work {get{return work;}}
        public AutoNarration(NarrationBudget budget,Func<AutoRequest,CancellationToken,Task> speak,Action<string> state){this.budget=budget;this.speak=speak;this.state=state;}
        public bool Enable(bool consent,bool credentials){Enabled=consent && credentials;return Enabled;}
        void Notify(){var handler=Changed;if(handler!=null)handler();}
        void Remember(string id){if(seen.Add(id)){seenOrder.Enqueue(id);if(seenOrder.Count>256)seen.Remove(seenOrder.Dequeue());}}
        static bool Active(NarrationEntry e){return e.State=="queued" || e.State=="playing";}
        static bool SameVoice(NarrationEntry a,NarrationEntry b){return a.Message.Session==b.Message.Session && a.Provider==b.Provider && a.Rate==b.Rate && a.Voice.Identity==b.Voice.Identity;}
        void Add(NarrationEntry e){entries.Add(e);pending.Add(e);}
        void Interrupt(bool cancel){foreach(var e in pending)if(Active(e))e.State="interrupted";pending.Clear();foreach(var e in candidates)e.State="interrupted";candidates.Clear();if(playing!=null && Active(playing))playing.State="interrupted";if(cancel && current!=null)current.Cancel();}
        int PendingSegments(){int n=0;foreach(var e in pending)n+=e.Parts.Count-e.Played;foreach(var e in candidates)n+=e.Parts.Count;return n;}
        void ResolveCandidates(){
            foreach(var entry in candidates.ToArray()){
                if(!entry.Complete || !candidates.Contains(entry))continue;
                if(!entry.Manual && RecentlyCompleted(entry.Message,entry.Text,entry.Voice,entry.Provider,entry.Rate)){candidates.Remove(entry);entries.Remove(entry);state("duplicate");continue;}
                if(pending.Exists(e=>Active(e) && !e.Complete && SameVoice(e,entry) && e.Message.TotalBytes==entry.Message.TotalBytes && e.Message.TextChecksum==entry.Message.TextChecksum))continue;
                candidates.Remove(entry);entry.Candidate=false;
                if(!entry.Manual && pending.Exists(e=>Active(e) && e.Complete && SameVoice(e,entry) && e.Text==entry.Text)){
                    entries.Remove(entry);state("duplicate");continue;
                }
                if(!QueueMode && !entry.ProtectedChain && !SameNpcTaskPending(entry.Message))Interrupt(true);pending.Add(entry);
            }
        }
        public void Accept(AutoRequest request){
            if(!Enabled || request==null || request.Message==null)return;
            var m=request.Message;bool segment=m.DialogueId!=null;
            // Voice lookup may enrich race/sex; validate transport identity before that enrichment.
            if(segment)AutoSegments.Validate(m);else GameReceipt.ValidateText(m.Text);
            if(request.Rate<0.6m || request.Rate>2m)throw new ArgumentOutOfRangeException("rate");
            string text=segment?m.Text:m.Text.Replace("\r","").Trim();
            bool manual=AutoMetadata.Manual(m),protect=Protect(m);
            string id=(m.Session??"")+"|"+(segment?"id:"+m.DialogueId:"text:"+text);
            NarrationEntry entry=null;
            if(!segment || m.SegmentIndex==0){
                request.Voice=request.Voice??(ResolveVoice==null?SpeakerVoiceRouting.Resolve(request.Provider,m):ResolveVoice(request));
                id+="|"+request.Provider+"|"+request.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+request.Voice.Identity;
                if(seen.Contains(id)){state("duplicate");return;}
                if(!manual && (!segment || m.SegmentCount==1) && RecentlyCompleted(m,text,request.Voice,request.Provider,request.Rate)){Remember(id);state("duplicate");return;}
                bool candidate=!manual && segment && m.SegmentCount>1 && PossibleRecent(m);
                foreach(var e in pending)if(!manual && Active(e) && e.Message.Session==m.Session && e.Provider==request.Provider && e.Rate==request.Rate && e.Voice.Identity==request.Voice.Identity &&
                    (segment && e.Message.DialogueId!=null ? e.Message.TotalBytes==m.TotalBytes && e.Message.TextChecksum==m.TextChecksum : e.Text==text)){
                    if((!segment || m.SegmentCount==1) && e.Complete && e.Text==text){Remember(id);state("duplicate");return;}
                    if(segment && (m.SegmentCount>1 || !e.Complete))candidate=true;
                }
                if(!candidate && !QueueMode && !protect && !SameNpcTaskPending(m))Interrupt(true);
                entry=new NarrationEntry {Id=id,Text="",Provider=request.Provider,Rate=request.Rate,Voice=request.Voice,State="queued",Message=CopyMessage(m),Manual=manual,ProtectedChain=protect};
                Remember(id);
                if(pending.Count+candidates.Count>=16 || PendingSegments()>=64){entry.Text=text;entry.State="failed";entries.Add(entry);Notify();state("narration-queue");return;}
                if(candidate){entry.Candidate=true;entries.Add(entry);candidates.Add(entry);}else Add(entry);
            }else{
                // Continuations use the latest admitted wire dialogue, not current settings.
                entry=entries.FindLast(e=>e.Message.Session==m.Session && e.Message.DialogueId==m.DialogueId && Active(e));if(entry==null || m.SegmentIndex!=entry.Parts.Count)return;
            }
            if(PendingSegments()>=64){entry.State="failed";pending.Remove(entry);candidates.Remove(entry);Notify();state("narration-queue");return;}
            var copy=CopyMessage(m);copy.Text=text;
            try{
                if(segment)entry.Assembly.Accept(copy);
            }catch{entry.State="failed";pending.Remove(entry);candidates.Remove(entry);if(playing==entry && current!=null)current.Cancel();Notify();throw;}
            entry.Parts.Add(new AutoRequest {Message=copy,Rate=entry.Rate,Provider=entry.Provider,Voice=entry.Voice});entry.Text+=text;entry.Complete=!segment || !entry.Assembly.Incomplete;
            ResolveCandidates();
            Notify();if(!pumping)work=Pump();
        }
        // Replay adds a history record and retains the original voice snapshot.
        static GameMessage CopyMessage(GameMessage source){var copy=new GameMessage();foreach(var property in typeof(GameMessage).GetProperties())if(property.CanRead && property.CanWrite)property.SetValue(copy,property.GetValue(source,null),null);return copy;}
        public void Replay(NarrationEntry source,string provider=null,VoiceSnapshot voice=null){
            if(!Enabled || source==null || !source.Complete || source.Parts.Count==0)return;
            if(source==playing && Active(source)){state("duplicate");return;}
            Interrupt(true);
            string fresh=Guid.NewGuid().ToString("N").Substring(0,24);
            var entry=new NarrationEntry {Id="id:"+fresh,Text=source.Text,Provider=provider??source.Provider,Rate=source.Rate,Voice=voice??source.Voice,State="queued",Complete=true,Manual=true};
            foreach(var part in source.Parts){
                var message=new GameMessage();
                foreach(var property in typeof(GameMessage).GetProperties())if(property.CanRead && property.CanWrite)property.SetValue(message,property.GetValue(part.Message,null),null);
                message.DialogueId=fresh;
                entry.Parts.Add(new AutoRequest {Message=message,Provider=entry.Provider,Rate=source.Rate,Voice=entry.Voice});
            }
            entry.Message=entry.Parts[0].Message;Remember(entry.Id);Add(entry);Notify();if(!pumping)work=Pump();
        }
        async Task Pump(){
            pumping=true;
            try{
                while(Enabled && pending.Count>0){
                    var entry=pending[0];
                    if(!Active(entry)){pending.RemoveAt(0);continue;}
                    var parts=entry.Parts;
                    if(entry.Voice.IsQwen){
                        // Transport fragments are not speech boundaries. Validate the whole dialogue first.
                        if(!entry.Complete)break;
                        if(entry.SpeechParts==null){
                            entry.SpeechParts=new List<AutoRequest>();var texts=SpeechText.Split(entry.Text,600);
                            for(int i=0;i<texts.Count;i++){
                                var message=CopyMessage(entry.Message);message.Text=texts[i];message.SegmentIndex=i;message.SegmentCount=texts.Count;
                                entry.SpeechParts.Add(new AutoRequest {Message=message,Rate=entry.Rate,Provider=entry.Provider,Voice=entry.Voice});
                            }
                        }
                        parts=entry.SpeechParts;
                    }
                    if(entry.Played>=parts.Count){if(entry.Complete){entry.State="completed";pending.RemoveAt(0);PruneRecent();recent.Add(new Recent {Entry=entry,Finished=UtcNow()});if(recent.Count>256)recent.RemoveAt(0);Notify();continue;}break;}
                    var request=parts[entry.Played++];entry.State="playing";playing=entry;Notify();
                    if(String.IsNullOrWhiteSpace(request.Message.Text))continue;
                    budget.Take();var tokenSource=new CancellationTokenSource(TimeSpan.FromSeconds(entry.Voice.IsQwen?600:60));current=tokenSource;
                    try{await speak(request,tokenSource.Token);}
                    catch{
                        if(Active(entry)){
                            entry.State="failed";Enabled=false;
                            Interrupt(false);state("speech-failed");
                        }
                    }
                    finally{if(current==tokenSource)current=null;tokenSource.Dispose();playing=null;}
                    Notify();
                }
            }finally{pumping=false;}
        }
        public void Disable(){Enabled=false;Interrupt(false);Notify();}
        public void Stop(){Enabled=false;Interrupt(true);Notify();}
        public void ClearHistory(){
            if(pumping || playing!=null || pending.Count>0 || candidates.Count>0)throw new InvalidOperationException("Playback must finish stopping before clearing history.");
            entries.Clear();seen.Clear();seenOrder.Clear();recent.Clear();Notify();
        }
    }
}
