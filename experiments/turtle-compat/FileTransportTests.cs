using System;
using System.IO;
using System.Text;
using VoicedAdventures.Turtle;
class FileTransportTests {
    static void Check(bool value,string name){if(!value)throw new Exception(name);}
    static int Main(string[] args) {
        string dir=Path.Combine(Path.GetTempPath(),"VA-file-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string token=new string('a',32),path=Path.Combine(dir,"VoicedAdventures_outbox.txt");
            var transport=new FileTransport(dir,dir,token);byte[] result=null;int index=0;
            foreach(string packet in File.ReadAllLines(args[0])) {
                string body="VA_FILE_2\n"+token+"\n0000000000000001\n"+(++index)+"\n"+packet+"\nEND\n";
                File.WriteAllText(path,body.Substring(0,body.Length-3));
                Check(transport.Poll(DateTime.UtcNow.Ticks)==null,"partial export ignored");
                File.WriteAllText(path,body.Replace(token,new string('b',32)));
                Check(transport.Poll(DateTime.UtcNow.Ticks)==null,"stale session ignored");
                File.WriteAllText(path,body);result=transport.Poll(DateTime.UtcNow.Ticks);
                Check(File.ReadAllText(Path.Combine(dir,"VoicedAdventures_ack.txt"))==token+":0000000000000001:"+index,"ack");
                Check(transport.Poll(DateTime.UtcNow.Ticks)==null,"duplicate ignored");
            }
            Check(result!=null && Encoding.UTF8.GetString(result).StartsWith("NPC\n"),"complete text");
            // A character switch resets the Lua sequence while the receiver stays alive.
            index=0;result=null;
            foreach(string packet in File.ReadAllLines(args[0])) {
                File.WriteAllText(path,"VA_FILE_2\n"+token+"\n0000000000000002\n"+(++index)+"\n"+packet+"\nEND\n");
                result=transport.Poll(DateTime.UtcNow.Ticks);
            }
            Check(result!=null,"new character must deliver without restarting assistant");
            string ack=File.ReadAllText(Path.Combine(dir,"VoicedAdventures_ack.txt"));
            File.WriteAllText(path,"VA_FILE_2\n"+token+"\n0000000000000001\n1\n"+File.ReadAllLines(args[0])[0]+"\nEND\n");
            Check(transport.Poll(DateTime.UtcNow.Ticks)==null,"retired character excluded");
            Check(File.ReadAllText(Path.Combine(dir,"VoicedAdventures_ack.txt"))==ack,"old client cannot reset ack");
            Console.WriteLine("PASS file output roundtrip, partial writes, stale session, ack and duplicates");return 0;
        } finally {foreach(string file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}
    }
}
