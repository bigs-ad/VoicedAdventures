using System;
using System.IO;
using System.Text;
using QuestVoiceStreaming;
using VoicedAdventures.Turtle;
class IntegrationTests {
    static int Main(string[] args) {
        string root=Path.GetFullPath(@"build\turtle-compat\fixture\Sounds");
        var packets=new PacketTransport(root);var decoder=new AutoFrameDecoder();
        var segments=new AutoSegments();var text=new StringBuilder();int control=0,metadata=0,count=0;
        foreach(string source in File.ReadAllLines(args[0])) {
            var bytes=packets.Accept(Path.Combine(root,Path.GetFileName(source)),DateTime.UtcNow.Ticks);
            if(bytes==null)continue;
            decoder.Add(16);foreach(byte b in bytes){decoder.Add(b>>4);decoder.Add(b&15);}
            var frame=decoder.Add(17);
            if(frame.Control){GameControl.Parse(frame.Text);control++;}
            else if(frame.Metadata!=null){if(frame.Metadata.SpeakerRace!="亡灵"||frame.Metadata.SpeakerSex!="male")throw new Exception("speaker metadata");metadata++;}
            else if(frame.Segment!=null){if(!segments.Accept(frame.Segment))throw new Exception("segments");text.Append(frame.Text);count++;}
            else throw new Exception("Unexpected frame");
        }
        var expected=new StringBuilder();for(int i=0;i<30;i++)expected.Append("这是一段任务文本。");
        if(control!=1||metadata!=1||count<2||segments.Incomplete||text.ToString()!=expected.ToString())throw new Exception("integration mismatch");
        Console.WriteLine("PASS legacy addon -> packet transport -> existing settings, speaker and complete quest protocol");return 0;
    }
}
