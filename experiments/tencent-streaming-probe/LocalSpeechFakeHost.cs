using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
class LocalSpeechFakeHost {
    static int Main(string[] args){
        Console.InputEncoding=new UTF8Encoding(false,true);
        if(args.Length==1&&args[0]=="--selected"){Console.WriteLine("fake-zh-CN");return 0;}
        if(args.Length<1||args.Length>3)return 9;
        if(args.Length==3&&args[2]!="fake-zh-CN")return 7;
        string text=Console.In.ReadToEnd();
        if(args[0]=="0.6"){Console.Error.WriteLine("no-chinese-voice");return 2;}
        if(args[0]=="0.7"){
            File.WriteAllText(text+".tmp",Process.GetCurrentProcess().Id.ToString());
            File.Move(text+".tmp",text);Thread.Sleep(60000);return 0;
        }
        if(args.Length>=2){using(var output=Console.OpenStandardOutput()){
            if(args[0]=="0.8"){output.WriteByte(0);return 0;}
            if(args[0]=="0.9"){var large=new byte[4*1024*1024+2];output.Write(large,0,large.Length);return 0;}
            output.Write(new byte[]{0,1,2,3},0,4);
        }return 0;}
        return Encoding.UTF8.GetByteCount(text)==4096?0:8;
    }
}
