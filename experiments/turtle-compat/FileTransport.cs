using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VoicedAdventures.Turtle {
    public sealed class FileTransport {
        readonly string directory, root, token;
        PacketTransport packets;
        string client;
        readonly HashSet<string> retired=new HashSet<string>();
        long sequence;
        public FileTransport(string imports, string sounds, string session) {
            directory=imports;root=sounds;token=session;
            packets=new PacketTransport(sounds);
            Directory.CreateDirectory(imports);
            File.WriteAllText(Path.Combine(directory,"VoicedAdventures_session.txt"),token,new UTF8Encoding(false));
        }
        public byte[] Poll(long ticks) {
            string path=Path.Combine(directory,"VoicedAdventures_outbox.txt");
            string text;
            try {
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
                    if(stream.Length>2048)return null;
                    using(var reader=new StreamReader(stream,new UTF8Encoding(false,true)))text=reader.ReadToEnd();
                }
            } catch(IOException){return null;}
            var lines=text.Replace("\r\n","\n").Split('\n');long next;
            if(lines.Length!=7 || lines[0]!="VA_FILE_2" || lines[1]!=token || lines[5]!="END" || lines[6]!="" ||
                !Regex.IsMatch(lines[2],"\\A[0-9A-F]{16}\\z") ||
                !Int64.TryParse(lines[3],out next) || next<1 || next>1000000000)return null;
            const string prefix=@"Interface\AddOns\VoicedAdventures\Sounds\";
            if(!lines[4].StartsWith(prefix,StringComparison.Ordinal) || lines[4].Substring(prefix.Length).IndexOfAny(new[]{'\\','/'})>=0)return null;
            if(lines[2]!=client){
                if(next!=1 || retired.Contains(lines[2]))return null;
                if(retired.Count>=1024)throw new InvalidDataException("file-session-budget");
                if(client!=null)retired.Add(client);
                client=lines[2];sequence=0;packets=new PacketTransport(root);
            }
            if(next==sequence){Ack();return null;}
            if(next!=sequence+1)return null;
            byte[] value=packets.Accept(Path.Combine(root,lines[4].Substring(prefix.Length)),ticks);
            sequence=next;Ack();return value;
        }
        void Ack(){File.WriteAllText(Path.Combine(directory,"VoicedAdventures_ack.txt"),token+":"+client+":"+sequence,new UTF8Encoding(false));}
    }
}
