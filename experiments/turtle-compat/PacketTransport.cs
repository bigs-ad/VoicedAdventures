using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VoicedAdventures.Turtle {
    // Only consume events already scoped to the verified game PID and addon directory.
    public sealed class PacketTransport {
        static readonly Regex Pattern=new Regex(@"^VA1_([0-9A-F]{16})_([0-9A-F]{3})_([0-9A-F]{3})_([0-9A-F]{8})_([0-9A-F]{2,96})\.wav$",RegexOptions.CultureInvariant);
        readonly string root;
        string id;
        int count,next;
        uint expected;
        long lastTicks;
        readonly List<byte> bytes=new List<byte>();
        readonly HashSet<string> completed=new HashSet<string>();
        readonly Queue<string> recent=new Queue<string>();
        public PacketTransport(string soundsDirectory) {
            root=Path.GetFullPath(soundsDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        }
        public byte[] Accept(string path,long utcTicks) {
            if(path==null || !path.StartsWith(root,StringComparison.OrdinalIgnoreCase))return null;
            string leaf=path.Substring(root.Length);
            var match=Pattern.Match(leaf);if(!match.Success)return null;
            if(utcTicks<=0)throw new InvalidDataException("packet-time");
            string incoming=match.Groups[1].Value;
            int index=Convert.ToInt32(match.Groups[2].Value,16),total=Convert.ToInt32(match.Groups[3].Value,16);
            uint sum=Convert.ToUInt32(match.Groups[4].Value,16);
            string hex=match.Groups[5].Value;
            if(total<1 || total>171 || index<1 || index>total || hex.Length%2!=0 || (index<total && hex.Length!=96))throw new InvalidDataException("packet-bounds");
            if(completed.Contains(incoming))return null;
            if(index==1 && incoming!=id){id=incoming;count=total;expected=sum;next=1;bytes.Clear();lastTicks=utcTicks;}
            if(incoming!=id)return null;
            if(total!=count || sum!=expected || utcTicks<lastTicks || utcTicks-lastTicks>TimeSpan.TicksPerSecond*15) {Reset();throw new InvalidDataException("packet-header-or-timeout");}
            if(index<next)return null;
            if(index!=next){Reset();throw new InvalidDataException("packet-order");}
            for(int i=0;i<hex.Length;i+=2)bytes.Add(Convert.ToByte(hex.Substring(i,2),16));
            lastTicks=utcTicks;next++;
            if(bytes.Count>8192){Reset();throw new InvalidDataException("packet-size");}
            if(index!=total)return null;
            byte[] result=bytes.ToArray();uint a=1,b=0;
            foreach(byte value in result){a=(a+value)%65521;b=(b+a)%65521;}
            if(((b<<16)|a)!=expected){Reset();throw new InvalidDataException("packet-checksum");}
            completed.Add(incoming);recent.Enqueue(incoming);
            if(recent.Count>256)completed.Remove(recent.Dequeue());
            Reset();return result;
        }
        void Reset(){id=null;bytes.Clear();count=next=0;lastTicks=0;}
    }
}
