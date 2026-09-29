using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace QuestVoiceStreaming {
 public static class LocalSpeech {
  static string Helper {get{return Path.Combine(AppContext.BaseDirectory,"LocalSpeechHost.exe");}}
  public static Task Run(string text,decimal rate,CancellationToken ct){return Run(text,rate,ct,Helper);}
  internal static async Task Run(string text,decimal rate,CancellationToken ct,string helper){Validate(text,rate);await Execute(text,new[]{rate.ToString(CultureInfo.InvariantCulture)},ct,helper,4096).ConfigureAwait(false);}
  public static Task<string> GetVoiceIdentity(CancellationToken ct){return GetVoiceIdentity(ct,Helper);}
  public static async Task<string[]> GetInstalledVoices(CancellationToken ct){
   string inventory=new UTF8Encoding(false,true).GetString(await Execute(null,new[]{"--voices"},ct,Helper,65536).ConfigureAwait(false));
   return inventory.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
  }
  internal static async Task<string> GetVoiceIdentity(CancellationToken ct,string helper){
   string identity=new UTF8Encoding(false,true).GetString(await Execute(null,new[]{"--selected"},ct,helper,4096).ConfigureAwait(false)).Trim();
   if(String.IsNullOrWhiteSpace(identity))throw new InvalidOperationException("Local voice identity missing.");return identity;
  }
  public static Task<byte[]> SynthesizePcm(string text,decimal rate,CancellationToken ct){return SynthesizePcmWithHelper(text,rate,ct,Helper,null);}
  public static Task<byte[]> SynthesizePcm(string text,decimal rate,CancellationToken ct,string expectedVoice){return SynthesizePcmWithHelper(text,rate,ct,Helper,expectedVoice);}
  internal static async Task<byte[]> SynthesizePcmWithHelper(string text,decimal rate,CancellationToken ct,string helper,string expectedVoice){
   Validate(text,rate);string rateText=rate.ToString(CultureInfo.InvariantCulture);
   byte[] pcm=await Execute(text,expectedVoice==null?new[]{rateText,"--pcm"}:new[]{rateText,"--pcm",expectedVoice},ct,helper,4*1024*1024).ConfigureAwait(false);
   if(pcm.Length==0||(pcm.Length&1)!=0)throw new InvalidOperationException("Local speech returned invalid PCM.");return pcm;
  }
  static void Validate(string text,decimal rate){
   if(String.IsNullOrWhiteSpace(text))throw new ArgumentException("Speech text is required.","text");
   if(new UTF8Encoding(false,true).GetByteCount(text)>4096)throw new ArgumentException("Speech text exceeds 4096 UTF-8 bytes.","text");
   if(rate<0.6m||rate>2m)throw new ArgumentOutOfRangeException("rate");
  }
  static async Task<byte[]> ReadBounded(Stream input,int maximum,Process child){
   using(var data=new MemoryStream()){
    byte[] buffer=new byte[8192];int count;
    while((count=await input.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false))!=0){
     if(data.Length+count>maximum){try{child.Kill();}catch(InvalidOperationException){}throw new InvalidOperationException("Local speech helper output exceeds limit.");}
     data.Write(buffer,0,count);
    }return data.ToArray();
   }
  }
  static async Task<byte[]> Execute(string text,string[] args,CancellationToken ct,string helper,int maximum){
   ct.ThrowIfCancellationRequested();
   var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true,StandardInputEncoding=new UTF8Encoding(false,true)};
   foreach(string arg in args)start.ArgumentList.Add(arg);
   using(var child=new Process{StartInfo=start}){
    if(!child.Start())throw new InvalidOperationException("Local speech helper could not start.");
    var error=ReadBounded(child.StandardError.BaseStream,16384,child);var output=ReadBounded(child.StandardOutput.BaseStream,maximum,child);
    ExceptionDispatchInfo failure=null;byte[] result=null;
    try {
     if(text!=null)await child.StandardInput.WriteAsync(text.AsMemory(),ct).ConfigureAwait(false);
     child.StandardInput.Close();await child.WaitForExitAsync(ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
     result=await output.ConfigureAwait(false);byte[] details=await error.ConfigureAwait(false);
     if(child.ExitCode!=0)throw new InvalidOperationException("Local speech failed: "+Encoding.UTF8.GetString(details).Trim());
    }catch(Exception ex){failure=ExceptionDispatchInfo.Capture(ex);}
    try{if(!child.HasExited)child.Kill();}catch(InvalidOperationException){}
    await child.WaitForExitAsync().ConfigureAwait(false);
    try{await Task.WhenAll(error,output).ConfigureAwait(false);}catch(Exception ex){if(failure==null)failure=ExceptionDispatchInfo.Capture(ex);}
    if(failure!=null)failure.Throw();return result;
   }
  }
 }
}
