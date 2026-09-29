using System;
using System.IO;
using QuestVoiceStreaming;
class SessionAudioCacheTests {
 static void Check(bool ok){if(!ok)throw new Exception("cache assertion");}
 static void Reject(Action a){try{a();}catch(IOException){return;}catch(ArgumentException){return;}throw new Exception("expected rejection");}
 static void Main(){
  string root=Path.Combine(Path.GetTempPath(),"qv-cache-test-"+Guid.NewGuid().ToString("N"));
  try {
   string key=SessionAudioCache.Key("system","actual-voice",1m,"hello");byte[] data;
   Check(key==SessionAudioCache.Key("system","actual-voice",1.0m,"hello"));
   Check(key!=SessionAudioCache.Key("system","other-voice",1m,"hello"));
   Check(key!=SessionAudioCache.Key("tencent","actual-voice",1m,"hello"));
   Check(key!=SessionAudioCache.Key("qwen","actual-voice",1m,"hello"));
   Check(SessionAudioCache.Key("qwen","model:voice",1m,"hello")!=SessionAudioCache.Key("qwen","model:voice",1.2m,"hello"));
   Check(key!=SessionAudioCache.Key("system","actual-voice",1.1m,"hello"));
   Check(key!=SessionAudioCache.Key("system","actual-voice",1m,"other"));
   Check(SessionAudioCache.Key("tencent","601008:neutral:100",1m,"同文")!=SessionAudioCache.Key("tencent","501002:neutral:100",1m,"同文"));
   using(var cache=new SessionAudioCache(root)){
    Check(!cache.TryRead(key,out data));cache.Commit(key,new byte[]{1,2});Check(cache.TryRead(key,out data)&&data.Length==2&&data[1]==2);
    Reject(()=>{using(var other=new SessionAudioCache(root)){};});
    Reject(()=>cache.Commit(key,new byte[3]));Reject(()=>cache.Commit("../escape",new byte[2]));
    Reject(()=>cache.Commit(key,new byte[0]));Reject(()=>cache.Commit(key,new byte[32*1024*1024+2]));
    string longKey=SessionAudioCache.Key("qwen","clone",0.6m,"whole dialogue");
    cache.Commit(longKey,new byte[8*1024*1024]);Check(cache.TryRead(longKey,out data)&&data.Length==8*1024*1024);
    string partialKey=SessionAudioCache.Key("system","actual-voice",1m,"partial");
    File.WriteAllBytes(Path.Combine(root,partialKey+"."+Guid.NewGuid().ToString("N")+".partial"),new byte[]{1,2});
    Check(!cache.TryRead(partialKey,out data));
    Check(cache.TryRead(key,out data));
    cache.Clear();Check(!cache.TryRead(key,out data));
    Check(!File.Exists(Path.Combine(root,key+".pcm")) && Directory.GetFiles(root,"*.partial").Length==0);
    Reject(()=>{using(var other=new SessionAudioCache(root)){};});
    cache.Commit(key,new byte[]{3,4});Check(cache.TryRead(key,out data));
    File.WriteAllText(Path.Combine(root,"keep.txt"),"foreign");Reject(()=>cache.Clear());
    Check(File.Exists(Path.Combine(root,"keep.txt")) && cache.TryRead(key,out data));File.Delete(Path.Combine(root,"keep.txt"));
   }
   string abandoned=Path.Combine(root,key+"."+Guid.NewGuid().ToString("N")+".partial");File.WriteAllBytes(abandoned,new byte[]{8,9});
   using(var cache=new SessionAudioCache(root,true)){
    Check(cache.TryRead(key,out data)&&data.Length==2&&data[0]==3&&data[1]==4);
    Check(!File.Exists(abandoned));Reject(()=>{using(var other=new SessionAudioCache(root,true)){};});
    cache.Commit(key,new byte[]{5,6});
   }
   using(var cache=new SessionAudioCache(root,true)){Check(cache.TryRead(key,out data)&&data[0]==5&&data[1]==6);}
   using(var cache=new SessionAudioCache(root)){Check(!cache.TryRead(key,out data));Check(!File.Exists(Path.Combine(root,key+".pcm")));Check(Directory.GetFiles(root,"*.partial").Length==0);}
   File.WriteAllText(Path.Combine(root,"foreign.txt"),"keep");Reject(()=>{using(var cache=new SessionAudioCache(root)){};});Check(File.Exists(Path.Combine(root,"foreign.txt")));
   Console.WriteLine("Session cache tests passed (isolated temporary directory).");
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
