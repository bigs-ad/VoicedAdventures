using System;
using System.IO;
using System.Text;
using VoicedAdventures.Turtle;
class PacketTests {
    static void Check(bool value,string name){if(!value)throw new Exception(name);}
    static int Main(string[] args){
        string root=Path.GetFullPath(@"build\turtle-compat\fixture\Sounds");
        string[] source=File.ReadAllLines(args[0]);
        var paths=new string[source.Length];
        for(int i=0;i<source.Length;i++)paths[i]=Path.Combine(root,Path.GetFileName(source[i]));
        var decoder=new PacketTransport(root);byte[] result=null;long ticks=DateTime.UtcNow.Ticks;
        Check(decoder.Accept(Path.Combine(root,"..","outside.wav"),ticks)==null,"unrelated path");
        for(int i=0;i<paths.Length;i++){
            result=decoder.Accept(paths[i],ticks+i);
            Check(decoder.Accept(paths[i],ticks+i)==null,"duplicate suppressed");
        }
        string expected="NPC\n";for(int i=0;i<80;i++)expected+="任务文本，重复对话也应完整传输。";
        Check(result!=null && new UTF8Encoding(false,true).GetString(result)==expected,"Lua/C# full UTF8 round trip");
        foreach(string path in paths)Check(decoder.Accept(path,ticks+100)==null,"completed replay");
        decoder=new PacketTransport(root);decoder.Accept(paths[0],ticks);
        bool rejected=false;try{decoder.Accept(paths[2],ticks+1);}catch(InvalidDataException){rejected=true;}Check(rejected,"missing packet");
        decoder=new PacketTransport(root);decoder.Accept(paths[0],ticks);
        rejected=false;try{decoder.Accept(paths[1],ticks+TimeSpan.TicksPerSecond*16);}catch(InvalidDataException){rejected=true;}Check(rejected,"stale packet");
        decoder=new PacketTransport(root);
        rejected=false;try{foreach(string path in paths)decoder.Accept(path.Replace("0000000000000001_","0000000000000002_").Replace("_4E50430A","_4F50430A"),ticks++);}catch(InvalidDataException){rejected=true;}Check(rejected,"checksum mismatch");
        Console.WriteLine("PASS UTF8 cross-language, duplicates, replay, missing packet, timeout, checksum and path bounds");return 0;
    }
}
