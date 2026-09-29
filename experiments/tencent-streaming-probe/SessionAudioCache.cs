using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QuestVoiceStreaming {
 public sealed class SessionAudioCache : IDisposable {
  const int Maximum=32*1024*1024;
  const string Marker="QuestVoice session PCM cache v1";
  readonly string directory;FileStream ownership;
  static readonly Regex Hash=new Regex("^[a-f0-9]{64}$",RegexOptions.CultureInvariant);
  static readonly Regex Owned=new Regex("^[a-f0-9]{64}(\\.pcm|\\.[a-f0-9]{32}\\.partial)$",RegexOptions.CultureInvariant);
  static void Safe(string path){
   for(string p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
    if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unsafe cache reparse point.");
  }
  public SessionAudioCache(string path,bool persistent=false){
   directory=Path.GetFullPath(path);Safe(directory);Directory.CreateDirectory(directory);
   string marker=Path.Combine(directory,"owner.txt"),lockPath=Path.Combine(directory,"session.lock");Safe(marker);Safe(lockPath);
   ownership=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
   try {
    var entries=Directory.GetFileSystemEntries(directory);
    bool marked=File.Exists(marker);
    if(marked&&File.ReadAllText(marker)!=Marker)throw new IOException("Unknown cache owner.");
    foreach(string file in entries){
     Safe(file);string name=Path.GetFileName(file);
     if(Directory.Exists(file)|| (name!="session.lock"&&name!="owner.txt"&&(!marked||!Owned.IsMatch(name))))throw new IOException("Foreign cache content; cleanup refused.");
    }
    if(!marked)File.WriteAllText(marker,Marker);
    foreach(string file in entries)if(Owned.IsMatch(Path.GetFileName(file))&&(!persistent||!file.EndsWith(".pcm",StringComparison.Ordinal))){Safe(file);File.Delete(file);}
   }catch{ownership.Dispose();ownership=null;throw;}
  }
  public static string Key(string provider,string voice,decimal rate,string text){
   if((provider!="system"&&provider!="tencent"&&provider!="qwen")||String.IsNullOrWhiteSpace(voice)||text==null||rate<0.6m||rate>2m)throw new ArgumentException("Invalid audio cache identity.");
   using(var bytes=new MemoryStream()){
    using(var writer=new BinaryWriter(bytes,new UTF8Encoding(false,true),true)){
     writer.Write("QuestVoice-PCM-v1");writer.Write(provider);writer.Write(voice);writer.Write(rate.ToString("0.############################",CultureInfo.InvariantCulture));writer.Write(text);writer.Write(provider=="qwen"?"pcm-s16le-24000-mono":"pcm-s16le-16000-mono");
    }
    using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();
   }
  }
  string FileFor(string key){if(ownership==null)throw new ObjectDisposedException("SessionAudioCache");if(key==null||!Hash.IsMatch(key))throw new ArgumentException("Invalid cache key.");Safe(directory);string file=Path.Combine(directory,key+".pcm");Safe(file);return file;}
  static void Validate(int length){if(length==0||length>Maximum||(length&1)!=0)throw new IOException("Invalid PCM cache length.");}
  public bool TryRead(string key,out byte[] data){
   string file=FileFor(key);data=null;if(!File.Exists(file))return false;
   using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){
    if(input.Length>Maximum)throw new IOException("PCM cache too large.");Validate((int)input.Length);data=new byte[(int)input.Length];int read=0;
    while(read<data.Length){int n=input.Read(data,read,data.Length-read);if(n==0)throw new IOException("Incomplete PCM cache.");read+=n;}
   }return true;
  }
  public void Commit(string key,byte[] data){
   string file=FileFor(key);if(data==null)throw new ArgumentNullException("data");Validate(data.Length);
   string temp=Path.Combine(directory,key+"."+Guid.NewGuid().ToString("N")+".partial");
   try {
    using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(data,0,data.Length);output.Flush(true);}
    Safe(file);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
   }finally{if(File.Exists(temp)){Safe(temp);File.Delete(temp);}}
  }
  public void Clear(){
   if(ownership==null)throw new ObjectDisposedException("SessionAudioCache");
   Safe(directory);string marker=Path.Combine(directory,"owner.txt");Safe(marker);
   if(!File.Exists(marker)||File.ReadAllText(marker)!=Marker)throw new IOException("Unknown cache owner.");
   var entries=Directory.GetFileSystemEntries(directory);
   foreach(string file in entries){
    Safe(file);string name=Path.GetFileName(file);
    if(Directory.Exists(file)||(name!="session.lock"&&name!="owner.txt"&&!Owned.IsMatch(name)))throw new IOException("Foreign cache content; cleanup refused.");
   }
   foreach(string file in entries)if(Owned.IsMatch(Path.GetFileName(file))){Safe(file);File.Delete(file);}
  }
  public void Dispose(){if(ownership!=null){ownership.Dispose();ownership=null;}}
 }
}
