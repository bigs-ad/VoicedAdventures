using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using System.Threading.Tasks;

namespace QuestVoiceStreaming {
    public sealed class GameMessage {
        public int Version { get; set; }
        public string Session { get; set; }
        public string Kind { get; set; }
        public int Pid { get; set; }
        public long StartTicks { get; set; }
        public string Text { get; set; }
        public long FirstEventUtcTicks { get; set; }
        public long DecodeUtcTicks { get; set; }
        public long EventsLost { get; set; }
        public bool Clean { get; set; }
        public string Error { get; set; }
        public long Sequence { get; set; }
        public string DialogueId { get; set; }
        public string SpeakerName { get; set; }
        public string NpcId { get; set; }
        public string SpeakerRace { get; set; }
        public string SpeakerSex { get; set; }
        public string QuestTitle { get; set; }
        public string Source { get; set; }
        public string VerifiedRealm { get; internal set; }
        public int MetadataVersion { get; set; }
        public string GameRealm { get; set; }
        public string GameBuild { get; set; }
        public string PlayerName { get; set; }
        public string QuestId { get; set; }
        public int? QuestLevel { get; set; }
        public string QuestStage { get; set; }
        public string RaceSource { get; set; }
        public string SexSource { get; set; }
        public bool IdentityConflict { get; set; }
        public string SpeakerKind { get; set; }
        public string QuestLevelSource { get; set; }
        public int SegmentIndex { get; set; }
        public int SegmentCount { get; set; }
        public int TotalBytes { get; set; }
        public uint TextChecksum { get; set; }
    }
    public sealed class GameReceipt {
        readonly string session; readonly int pid; readonly long ticks; int state;
        public GameReceipt(string session,int pid,long ticks) { this.session=session; this.pid=pid; this.ticks=ticks; }
        public GameMessage Accept(GameMessage message) {
            if(message==null || message.Version!=1 || message.Session!=session || message.Pid!=pid || message.StartTicks!=ticks || state==2)
                throw new InvalidDataException("bridge-identity-or-replay");
            if(message.Kind=="ready" && state==0) { state=1; return null; }
            if(message.Kind=="failure") {
                state=2;
                if(!message.Clean) throw new InvalidDataException("receiver-cleanup-unconfirmed");
                if(message.EventsLost>0) throw new InvalidDataException("receiver-events-lost");
                if(message.Error=="receiver-timeout" || message.Error=="game-exited" || message.Error=="frame-invalid") throw new InvalidDataException(message.Error);
                throw new InvalidDataException("receiver-failed");
            }
            if(message.Kind!="result" || state!=1) throw new InvalidDataException("bridge-sequence");
            state=2;
            if(message.EventsLost!=0 || !message.Clean) throw new InvalidDataException("receiver-loss-or-cleanup");
            ValidateText(message.Text);
            if(message.FirstEventUtcTicks<=0 || message.DecodeUtcTicks<message.FirstEventUtcTicks || message.DecodeUtcTicks>DateTime.UtcNow.AddSeconds(2).Ticks)
                throw new InvalidDataException("bridge-timing");
            return message;
        }
        public static void ValidateText(string text) {
            if(String.IsNullOrWhiteSpace(text) || text.Length>255) throw new InvalidDataException("dialogue-size");
            foreach(char c in text) if(Char.IsControl(c) && c!='\r' && c!='\n' && c!='\t') throw new InvalidDataException("dialogue-control");
            try {
                int utf8=new UTF8Encoding(false,true).GetByteCount(text);
                int compact=1+new UnicodeEncoding(false,false,true).GetByteCount(text);
                if(Math.Min(utf8,compact)>255) throw new InvalidDataException("dialogue-too-long");
            } catch(EncoderFallbackException) { throw new InvalidDataException("dialogue-encoding"); }
        }
    }
    public static class GameWire {
        const int Maximum=8192;
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
        static async Task<bool> Fill(Stream stream,byte[] bytes,bool header,CancellationToken ct) {
            int offset=0;
            while(offset<bytes.Length) {
                ct.ThrowIfCancellationRequested();
                int read=await stream.ReadAsync(bytes,offset,bytes.Length-offset,ct).ConfigureAwait(false);
                if(read==0) { if(header && offset==0) return false; throw new InvalidDataException("bridge-truncated"); }
                offset+=read;
            }
            return true;
        }
        public static async Task<GameMessage> Read(Stream stream,CancellationToken ct) {
            byte[] header=new byte[4]; if(!await Fill(stream,header,true,ct).ConfigureAwait(false)) return null;
            int length=BitConverter.ToInt32(header,0);
            if(length<2 || length>Maximum) throw new InvalidDataException("bridge-size");
            byte[] payload=new byte[length]; await Fill(stream,payload,false,ct).ConfigureAwait(false);
            try {
                var value=JsonSerializer.Deserialize<GameMessage>(new UTF8Encoding(false,true).GetString(payload),new JsonSerializerOptions { MaxDepth=4,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
                if(value==null) throw new InvalidDataException("bridge-null");
                return value;
            } catch(JsonException) { throw new InvalidDataException("bridge-json"); }
              catch(DecoderFallbackException) { throw new InvalidDataException("bridge-utf8"); }
        }
        public static async Task Write(Stream stream,GameMessage message,CancellationToken ct) {
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(message);
            if(bytes.Length>Maximum) throw new InvalidDataException("bridge-size");
            await stream.WriteAsync(BitConverter.GetBytes(bytes.Length),0,4,ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes,0,bytes.Length,ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        public static NamedPipeServerStream CreateServer(string name) {
            var security=new PipeSecurity();
            using(var user=WindowsIdentity.GetCurrent()) {
                security.SetAccessRuleProtection(true,false);
                security.SetOwner(user.User);
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
                security.AddAccessRule(new PipeAccessRule(user.User,PipeAccessRights.FullControl,AccessControlType.Allow));
            }
            // Explicit SID ACL permits the same user across the UAC boundary, not other accounts.
            return NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,Maximum,Maximum,security);
        }
        public static void VerifyPeer(PipeStream pipe,int pid,bool server) {
            uint actual;
            bool ok=server?GetNamedPipeClientProcessId(pipe.SafePipeHandle,out actual):GetNamedPipeServerProcessId(pipe.SafePipeHandle,out actual);
            if(!ok || actual!=(uint)pid) throw new InvalidDataException("bridge-peer");
        }
    }
}
