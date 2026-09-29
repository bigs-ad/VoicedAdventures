using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
namespace VoicedAdventures.Qwen {
    sealed class FakeChannel : IQwenChannel {
        public readonly Queue<string> Incoming=new Queue<string>();
        public readonly List<string> Sent=new List<string>();
        public Task Connect(string key,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.CompletedTask;}
        public Task Send(string text,CancellationToken ct){ct.ThrowIfCancellationRequested();Sent.Add(text);return Task.CompletedTask;}
        public Task<string> Receive(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(Incoming.Count==0?null:Incoming.Dequeue());}
        public void Dispose(){}
    }
    static class Tests {
        static int count;
        static void Check(bool value){count++;if(!value)throw new Exception("Check failed "+count);}
        static void Reject(Action action){bool failed=false;try{action();}catch{failed=true;}Check(failed);}
        static FakeChannel Channel(params string[] tail){
            var c=new FakeChannel();c.Incoming.Enqueue("{\"type\":\"session.created\"}");c.Incoming.Enqueue("{\"type\":\"session.updated\"}");
            foreach(var s in tail)c.Incoming.Enqueue(s);return c;
        }
        public static void Main(){
            using(var session=JsonDocument.Parse(Protocol.Session("voice",1.2m)))Check(session.RootElement.GetProperty("session").GetProperty("speech_rate").GetDecimal()==1.2m);
            Reject(()=>Protocol.Session("voice",3m));
            Reject(()=>new QwenChannel("wrong"));
            using(var channelDefault=new QwenChannel())Check(channelDefault.Endpoint.Query.Contains(Protocol.Model));
            string audio="{\"type\":\"response.audio.delta\",\"delta\":\"AAABAA==\"}";
            string done="{\"type\":\"response.done\",\"response\":{\"status\":\"completed\"}}";
            string finish="{\"type\":\"session.finished\"}";
            var channel=Channel(audio,done,finish);int bytes=0;
            byte[] result=new QwenClient().Synthesize(channel,"dummy","test-voice","你好",b=>bytes+=b.Length,delegate{},CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Length==4 && bytes==4);Check(channel.Sent.Count==4);
            using(var doc=JsonDocument.Parse(channel.Sent[0]))Check(doc.RootElement.GetProperty("session").GetProperty("voice").GetString()=="test-voice");
            Reject(()=>new QwenClient().Synthesize(Channel(audio),"dummy","v","x",delegate{},delegate{},CancellationToken.None).GetAwaiter().GetResult());
            Reject(()=>new QwenClient().Synthesize(Channel(finish),"dummy","v","x",delegate{},delegate{},CancellationToken.None).GetAwaiter().GetResult());
            Reject(()=>new QwenClient().Synthesize(Channel("{\"type\":\"error\",\"error\":{\"message\":\"secret-content\"}}"),"dummy","v","x",delegate{},delegate{},CancellationToken.None).GetAwaiter().GetResult());
            Reject(()=>Protocol.ReadEvent("{\"type\":\"response.audio.delta\",\"delta\":\"AA==\"}"));
            Reject(()=>new QwenClient().Synthesize(Channel(audio,"{\"type\":\"response.done\",\"response\":{\"status\":\"failed\"}}",finish),"dummy","v","x",delegate{},delegate{},CancellationToken.None).GetAwaiter().GetResult());
            var cancel=new CancellationTokenSource();cancel.Cancel();Reject(()=>new QwenClient().Synthesize(Channel(),"dummy","v","x",delegate{},delegate{},cancel.Token).GetAwaiter().GetResult());
            string path=Path.Combine(Path.GetTempPath(),"va-qwen-test-"+Guid.NewGuid().ToString("N"));
            try {
                var store=new QwenStore(path);store.SaveKey("sk-dummy-test-only");
                Check(store.LoadKey()=="sk-dummy-test-only");Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(path,"key.bin"))).Contains("dummy"));
                store.SaveKey("sk-updated-test-only");Check(store.LoadKey()=="sk-updated-test-only");
                Reject(()=>store.SaveKey("dummy\nkey"));Check(store.LoadKey()=="sk-updated-test-only");
                File.WriteAllText(Path.Combine(path,"key.bin"),"broken");Reject(()=>store.LoadKey());
            }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
            Console.WriteLine("PASS Qwen offline: "+count+" assertions; no cloud or audible playback");
        }
    }
}
