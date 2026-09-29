using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace QuestVoiceStreaming {
    public sealed class GameControl {
        public string Provider;public int Rate;public bool Stop,QueueMode;
        public int? Volume;
        public static GameControl Parse(string text) {
            if(text=="stop")return new GameControl {Stop=true};
            var parts=(text??"").Split(':');int rate;
            if((parts.Length!=3 && parts.Length!=4 && parts.Length!=5) || parts[0]!="settings" || (parts[1]!="system" && parts[1]!="tencent") ||
                !Int32.TryParse(parts[2],out rate) || rate<6 || rate>20 || parts[2]!=rate.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new FormatException("game-control");
            if(parts.Length>=4 && parts[3]!="queue" && parts[3]!="interrupt")throw new FormatException("game-control");
            int volume=0;
            if(parts.Length==5 && (!Int32.TryParse(parts[4],out volume) || volume<0 || volume>200 || parts[4]!=volume.ToString(System.Globalization.CultureInfo.InvariantCulture)))throw new FormatException("game-control");
            return new GameControl {Provider=parts[1],Rate=rate,QueueMode=parts.Length>=4 && parts[3]=="queue",Volume=parts.Length==5?(int?)volume:null};
        }
    }
    public sealed class AutoFrame { public string Text,Error; public GameMessage Segment,Metadata; public bool Control; }
    public static class AutoMetadata {
        public static bool Manual(GameMessage m){return m!=null && (m.Source??"").EndsWith("-m",StringComparison.Ordinal);}
        public static bool TurnIn(GameMessage m){return m!=null && (m.Source=="turnin" || m.Source=="turnin-m");}
        public static string BaseSource(GameMessage m){
            string source=m==null?"":m.Source??"";
            if(source=="book-m")return "book";
            if(source=="quest-m")return "questlog";
            return source=="npc-m" || source=="turnin" || source=="turnin-m"?"npc":source;
        }
        static readonly int[] Limits={64,20,40,6,96,8,96,10,72,10,4,10,16,16,1,8,16};
        static bool OneOf(string value,params string[] allowed){return Array.IndexOf(allowed,value??"")>=0;}
        static void ValidateEvidence(GameMessage m){
            int id;
            if(!String.IsNullOrEmpty(m.QuestId) && (!Int32.TryParse(m.QuestId,out id) || id<=0 || id.ToString(System.Globalization.CultureInfo.InvariantCulture)!=m.QuestId))throw new InvalidDataException("metadata-quest-id");
            if(m.QuestLevel.HasValue && (m.QuestLevel.Value < -1 || m.QuestLevel.Value>9999))throw new InvalidDataException("metadata-quest-level");
            if(!OneOf(m.QuestStage,"","detail","progress","complete","gossip","greeting","questlog","book"))throw new InvalidDataException("metadata-quest-stage");
            foreach(string source in new[]{m.RaceSource,m.SexSource})
                if(!OneOf(source,"","unit","model","npc-table","observed","manual","legacy","unknown","document"))throw new InvalidDataException("metadata-provenance");
            if(!OneOf(m.SpeakerKind,"","npc","narrator") || !OneOf(m.QuestLevelSource,"","quest-api","quest-log","unknown"))throw new InvalidDataException("metadata-evidence");
        }
        public static void Copy(GameMessage source,GameMessage target) {
            target.SpeakerName=source==null?"":source.SpeakerName??"";target.NpcId=source==null?"":source.NpcId??"";
            target.SpeakerRace=source==null?"":source.SpeakerRace??"";target.SpeakerSex=source==null?"":source.SpeakerSex??"";
            target.QuestTitle=source==null?"":source.QuestTitle??"";target.Source=source==null?"":source.Source??"";
            target.MetadataVersion=source==null?0:source.MetadataVersion;
            target.GameRealm=source==null?"":source.GameRealm??"";target.GameBuild=source==null?"":source.GameBuild??"";target.PlayerName=source==null?"":source.PlayerName??"";
            target.QuestId=source==null?"":source.QuestId??"";target.QuestLevel=source==null?null:source.QuestLevel;
            target.QuestStage=source==null?"":source.QuestStage??"";target.RaceSource=source==null?"":source.RaceSource??"";target.SexSource=source==null?"":source.SexSource??"";
            target.IdentityConflict=source!=null && source.IdentityConflict;target.SpeakerKind=source==null?"":source.SpeakerKind??"";target.QuestLevelSource=source==null?"":source.QuestLevelSource??"";
        }
        public static byte[] Payload(GameMessage m) {
            if(m.DialogueId==null || m.DialogueId.Length!=24)throw new InvalidDataException("metadata-id");
            foreach(char c in m.DialogueId)if(!Uri.IsHexDigit(c))throw new InvalidDataException("metadata-id");
            if(m.MetadataVersion!=0 && m.MetadataVersion!=4 && m.MetadataVersion!=5 && m.MetadataVersion!=6)throw new InvalidDataException("metadata-version");
            bool extended=m.MetadataVersion==6,scoped=m.MetadataVersion>=5;
            var values=new[]{m.SpeakerName??"",m.NpcId??"",m.SpeakerRace??"",m.SpeakerSex??"",m.QuestTitle??"",m.Source??"",m.GameRealm??"",m.GameBuild??"",m.PlayerName??"",m.QuestId??"",m.QuestLevel.HasValue?m.QuestLevel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"",m.QuestStage??"",m.RaceSource??"",m.SexSource??"",m.IdentityConflict?"1":"",m.SpeakerKind??"",m.QuestLevelSource??""};
            if(extended)ValidateEvidence(m);
            else for(int i=9;i<values.Length;i++)if(values[i]!="")throw new InvalidDataException("metadata-extension-version");
            if(scoped){
                if(values[6].Length==0 || values[7].Length==0 || values[8].Length==0)throw new InvalidDataException("metadata-scope");
                foreach(char c in values[7])if(c<'0'||c>'9')throw new InvalidDataException("metadata-build");
            }else if(values[6].Length+values[7].Length+values[8].Length>0)throw new InvalidDataException("metadata-scope");
            if(values[3]!="" && values[3]!="male" && values[3]!="female" || values[5]!="" && values[5]!="npc" && values[5]!="questlog" && values[5]!="book" && values[5]!="npc-m" && values[5]!="quest-m" && values[5]!="book-m" && values[5]!="turnin" && values[5]!="turnin-m")throw new InvalidDataException("metadata-value");
            foreach(char c in values[1])if(c<'0'||c>'9')throw new InvalidDataException("metadata-npc");
            var bytes=new List<byte>();for(int i=0;i<12;i++)bytes.Add(Convert.ToByte(m.DialogueId.Substring(i*2,2),16));
            for(int i=0;i<(extended?17:scoped?9:6);i++) {
                foreach(char c in values[i])if(Char.IsControl(c))throw new InvalidDataException("metadata-control");
                byte[] field;try{field=new UTF8Encoding(false,true).GetBytes(values[i]);}catch(EncoderFallbackException){throw new InvalidDataException("metadata-encoding");}
                if(field.Length>Limits[i])throw new InvalidDataException("metadata-bound");bytes.Add((byte)field.Length);bytes.AddRange(field);
            }
            if(bytes.Count>255)throw new InvalidDataException("metadata-size");return bytes.ToArray();
        }
        public static GameMessage Decode(byte[] bytes,int version=4) {
            if(version!=4 && version!=5 && version!=6)throw new InvalidDataException("metadata-version");
            int count=version==6?17:version==5?9:6;
            if(bytes.Length<12+count || bytes.Length>255)throw new InvalidDataException("metadata-size");
            var values=new string[count];int offset=12;
            for(int i=0;i<count;i++) {
                if(offset>=bytes.Length)throw new InvalidDataException("metadata-truncated");int length=bytes[offset++];
                if(length>Limits[i] || offset+length>bytes.Length)throw new InvalidDataException("metadata-bound");
                try{values[i]=new UTF8Encoding(false,true).GetString(bytes,offset,length);}catch(DecoderFallbackException){throw new InvalidDataException("metadata-encoding");}offset+=length;
            }
            if(offset!=bytes.Length)throw new InvalidDataException("metadata-trailing");
            var m=new GameMessage {DialogueId=BitConverter.ToString(bytes,0,12).Replace("-",""),SpeakerName=values[0],NpcId=values[1],SpeakerRace=values[2],SpeakerSex=values[3],QuestTitle=values[4],Source=values[5],MetadataVersion=version};
            if(version>=5){m.GameRealm=values[6];m.GameBuild=values[7];m.PlayerName=values[8];}
            if(version==6){
                int level;
                if(values[10]!="" && (!Int32.TryParse(values[10],out level) || level.ToString(System.Globalization.CultureInfo.InvariantCulture)!=values[10]))throw new InvalidDataException("metadata-quest-level");
                if(values[14]!="" && values[14]!="1")throw new InvalidDataException("metadata-conflict");
                m.QuestId=values[9];m.QuestLevel=values[10]==""?(int?)null:Int32.Parse(values[10],System.Globalization.CultureInfo.InvariantCulture);
                m.QuestStage=values[11];m.RaceSource=values[12];m.SexSource=values[13];m.IdentityConflict=values[14]=="1";m.SpeakerKind=values[15];m.QuestLevelSource=values[16];
            }
            Payload(m);return m;
        }
        public static byte[] Encode(GameMessage m) {
            var payload=Payload(m);var frame=new byte[payload.Length+6];frame[0]=81;frame[1]=65;frame[2]=(byte)(m.MetadataVersion>=5?m.MetadataVersion:4);frame[3]=(byte)payload.Length;Array.Copy(payload,0,frame,4,payload.Length);
            int a=0,b=0;for(int i=0;i<frame.Length-2;i++){a=(a+frame[i])%255;b=(b+a)%255;}frame[frame.Length-2]=(byte)a;frame[frame.Length-1]=(byte)b;return frame;
        }
    }
    public sealed class AutoFrameDecoder {
        readonly List<byte> bytes=new List<byte>(); volatile bool receiving; int nibble=-1;
        public bool Receiving { get { return receiving; } }
        public AutoFrame Add(int symbol) {
            if(symbol==16) { bytes.Clear();nibble=-1;receiving=true;return null; }
            if(!receiving) return null;
            if(symbol==17) {
                receiving=false;
                if(nibble!=-1 || bytes.Count<6 || bytes.Count!=bytes[3]+6) throw new FormatException("auto-truncated");
                int a=0,b=0;for(int i=0;i<bytes.Count-2;i++){a=(a+bytes[i])%255;b=(b+a)%255;}
                if(bytes[bytes.Count-2]!=a || bytes[bytes.Count-1]!=b) throw new FormatException("auto-checksum");
                if(bytes[2]>=4 && bytes[2]<=6)return new AutoFrame {Metadata=AutoMetadata.Decode(bytes.GetRange(4,bytes[3]).ToArray(),bytes[2])};
                if(bytes[2]==2) {
                    if(bytes[3]<=20)throw new FormatException("segment-size");
                    var data=bytes.ToArray();
                    var m=new GameMessage {DialogueId=BitConverter.ToString(data,4,12).Replace("-",""),SegmentIndex=data[16],SegmentCount=data[17],
                        TotalBytes=data[18]+256*data[19],TextChecksum=AutoSegments.Read32(data,20),Text=FrameDecoder.DecodePayload(bytes.GetRange(24,bytes[3]-20).ToArray())};
                    AutoSegments.Validate(m);return new AutoFrame {Text=m.Text,Segment=m};
                }
                string text=FrameDecoder.DecodePayload(bytes.GetRange(4,bytes[3]).ToArray());
                if(bytes[2]==3) {GameControl.Parse(text);return new AutoFrame {Text=text,Control=true};}
                if(bytes[2]==1) {
                    if(text!="too-long" && text!="unavailable-text") throw new FormatException("auto-rejection");
                    return new AutoFrame {Error=text};
                }
                GameReceipt.ValidateText(text); return new AutoFrame {Text=text};
            }
            if(symbol<0 || symbol>15) throw new FormatException("auto-symbol");
            if(nibble==-1) { nibble=symbol;return null; }
            bytes.Add((byte)(nibble*16+symbol));nibble=-1;
            if((bytes.Count==1 && bytes[0]!=81) || (bytes.Count==2 && bytes[1]!=65) || (bytes.Count==3 && bytes[2]>6) ||
                (bytes.Count>=4 && bytes.Count>bytes[3]+6)) throw new FormatException("auto-header-or-overflow");
            return null;
        }
        public static byte[] Encode(string text,int kind) {
            if(kind<0 || kind>3 || kind==2) throw new ArgumentException("kind");
            if(kind==3)GameControl.Parse(text);
            byte[] payload=new UTF8Encoding(false,true).GetBytes(text);
            byte[] compact=new UnicodeEncoding(false,false,true).GetBytes(text);
            if(compact.Length+1<payload.Length){payload=new byte[compact.Length+1];payload[0]=255;Array.Copy(compact,0,payload,1,compact.Length);}
            if(payload.Length>255) throw new ArgumentException("payload");
            byte[] frame=new byte[payload.Length+6];frame[0]=81;frame[1]=65;frame[2]=(byte)kind;frame[3]=(byte)payload.Length;
            Array.Copy(payload,0,frame,4,payload.Length);int a=0,b=0;
            for(int i=0;i<frame.Length-2;i++){a=(a+frame[i])%255;b=(b+a)%255;}
            frame[frame.Length-2]=(byte)a;frame[frame.Length-1]=(byte)b;return frame;
        }
    }
    public sealed class AutoReceipt {
        readonly string session;readonly int pid;readonly long ticks;bool ready,finished;long sequence;
        readonly AutoSegments segments=new AutoSegments();
        GameMessage pendingMetadata,activeMetadata;
        public AutoReceipt(string session,int pid,long ticks) {this.session=session;this.pid=pid;this.ticks=ticks;}
        public GameMessage Accept(GameMessage m) {
            if(m==null || m.Version!=2 || m.Session!=session || m.Pid!=pid || m.StartTicks!=ticks || finished) throw new InvalidDataException("auto-identity");
            if(m.Kind=="ready" && !ready && m.Sequence==0){ready=true;return null;}
            if(m.Kind=="failure") {finished=true;throw new InvalidDataException(!m.Clean?"receiver-cleanup-unconfirmed":ready && m.Sequence==sequence+1 && m.EventsLost==0 && m.Error=="game-exited"?"game-exited-clean":"auto-receiver-failed");}
            if(!ready || sequence==Int64.MaxValue || m.Sequence!=sequence+1 || m.EventsLost!=0) throw new InvalidDataException("auto-order-or-loss");
            sequence=m.Sequence;
            if(m.Kind=="done") {finished=true;if(!m.Clean)throw new InvalidDataException("receiver-cleanup-unconfirmed");if(segments.Incomplete || pendingMetadata!=null)throw new InvalidDataException("receiver-incomplete");return null;}
            if(m.Kind=="metadata") {AutoMetadata.Payload(m);if(segments.Incomplete || pendingMetadata!=null)throw new InvalidDataException("metadata-order");pendingMetadata=AutoMetadata.Decode(AutoMetadata.Payload(m),m.MetadataVersion>=5?m.MetadataVersion:4);return null;}
            if(m.Kind=="rejected") {if(m.Error!="too-long" && m.Error!="unavailable-text")throw new InvalidDataException("auto-rejection");segments.Reset();pendingMetadata=activeMetadata=null;return m;}
            if(m.Kind=="control") {GameControl.Parse(m.Text);segments.Reset();pendingMetadata=activeMetadata=null;return m;}
            if(m.Kind!="text" || m.FirstEventUtcTicks<=0 || m.DecodeUtcTicks<m.FirstEventUtcTicks || m.DecodeUtcTicks>DateTime.UtcNow.AddSeconds(2).Ticks) throw new InvalidDataException("auto-frame");
            if(m.DialogueId!=null) {
                if(m.SegmentIndex==0) {
                    if(pendingMetadata!=null && !String.Equals(pendingMetadata.DialogueId,m.DialogueId,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("metadata-mismatch");
                    activeMetadata=pendingMetadata;pendingMetadata=null;
                }
                bool accepted=segments.Accept(m);AutoMetadata.Copy(activeMetadata,m);return accepted?m:null;
            }
            pendingMetadata=activeMetadata=null;AutoMetadata.Copy(null,m);GameReceipt.ValidateText(m.Text);return m;
        }
    }
    public sealed class AutoSegments {
        GameMessage header;int next;readonly StringBuilder text=new StringBuilder();
        public bool Incomplete {get{return header!=null && next<header.SegmentCount;}}
        public void Reset(){header=null;next=0;text.Clear();}
        public static uint Read32(byte[] b,int offset){return (uint)b[offset]|((uint)b[offset+1]<<8)|((uint)b[offset+2]<<16)|((uint)b[offset+3]<<24);}
        public static uint Checksum(string text) {
            uint a=1,b=0;foreach(byte value in new UTF8Encoding(false,true).GetBytes(text)){a=(a+value)%65521;b=(b+a)%65521;}return (b<<16)|a;
        }
        public static void Copy(GameMessage source,GameMessage target) {
            target.DialogueId=source.DialogueId;target.SegmentIndex=source.SegmentIndex;target.SegmentCount=source.SegmentCount;
            target.TotalBytes=source.TotalBytes;target.TextChecksum=source.TextChecksum;
        }
        public static void Validate(GameMessage m) {
            if(m.DialogueId==null || m.DialogueId.Length!=24)throw new InvalidDataException("segment-id");
            foreach(char c in m.DialogueId)if(!Uri.IsHexDigit(c))throw new InvalidDataException("segment-id");
            if(m.SegmentCount<1 || m.SegmentCount>64 || m.SegmentIndex<0 || m.SegmentIndex>=m.SegmentCount || m.TotalBytes<1 || m.TotalBytes>4096 || String.IsNullOrEmpty(m.Text))throw new InvalidDataException("segment-bound");
            foreach(char c in m.Text)if(Char.IsControl(c)&&c!='\n'&&c!='\r'&&c!='\t')throw new InvalidDataException("segment-control");
            try {if(Math.Min(new UTF8Encoding(false,true).GetByteCount(m.Text),1+new UnicodeEncoding(false,false,true).GetByteCount(m.Text))>235)throw new InvalidDataException("segment-size");}
            catch(EncoderFallbackException){throw new InvalidDataException("segment-encoding");}
        }
        public bool Accept(GameMessage m) {
            Validate(m);
            if(m.SegmentIndex==0) {
                if(Incomplete && header.DialogueId==m.DialogueId)throw new InvalidDataException("segment-replay");
                header=m;next=0;text.Clear();
            } else if(header==null)return false;
            if(header.DialogueId!=m.DialogueId || next!=m.SegmentIndex || header.SegmentCount!=m.SegmentCount ||
                header.TotalBytes!=m.TotalBytes || header.TextChecksum!=m.TextChecksum)throw new InvalidDataException("segment-order");
            text.Append(m.Text);next++;
            int length=new UTF8Encoding(false,true).GetByteCount(text.ToString());
            if(length>m.TotalBytes || (next==m.SegmentCount && (length!=m.TotalBytes || Checksum(text.ToString())!=m.TextChecksum)))throw new InvalidDataException("segment-integrity");
            return true;
        }
        // Only used by offline fixtures. Production frames originate in the addon.
        public static byte[] Encode(GameMessage m) {
            Validate(m);var plain=AutoFrameDecoder.Encode(m.Text,0);int size=plain[3];var data=new byte[26+size];
            data[0]=81;data[1]=65;data[2]=2;data[3]=(byte)(20+size);
            for(int i=0;i<12;i++)data[4+i]=Convert.ToByte(m.DialogueId.Substring(i*2,2),16);
            data[16]=(byte)m.SegmentIndex;data[17]=(byte)m.SegmentCount;data[18]=(byte)m.TotalBytes;data[19]=(byte)(m.TotalBytes>>8);
            for(int i=0;i<4;i++)data[20+i]=(byte)(m.TextChecksum>>(8*i));Array.Copy(plain,4,data,24,size);
            int a=0,b=0;for(int i=0;i<data.Length-2;i++){a=(a+data[i])%255;b=(b+a)%255;}data[data.Length-2]=(byte)a;data[data.Length-1]=(byte)b;return data;
        }
    }
}
