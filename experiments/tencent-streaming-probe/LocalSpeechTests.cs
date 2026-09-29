using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QuestVoiceStreaming;

class LocalSpeechTests {
    static int count;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
    static async Task Reject(Func<Task> action,string label){try{await action();}catch(ArgumentException){count++;return;}throw new Exception(label);}
    static void Main(string[] args){Run(args[0]).GetAwaiter().GetResult();}
    static async Task HostReject(string helper,string rate,string text,string expected){
        using(var child=Process.Start(new ProcessStartInfo(helper,rate){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true,StandardInputEncoding=new System.Text.UTF8Encoding(false)})){
            Task<string> error=child.StandardError.ReadToEndAsync();
            await child.StandardInput.WriteAsync(text);child.StandardInput.Close();
            await child.WaitForExitAsync();string details=await error;Check(child.ExitCode!=0&&details.Contains(expected),"real helper rejects "+expected+": "+details);
        }
    }
    static async Task Run(string helper){
        string dir=Path.Combine(Path.GetTempPath(),"qv-local-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try {
            Check(await LocalSpeech.GetVoiceIdentity(CancellationToken.None,helper)=="fake-zh-CN","actual selected voice identity");
            byte[] pcm=await LocalSpeech.SynthesizePcmWithHelper("silent fixture",1m,CancellationToken.None,helper,"fake-zh-CN");
            Check(pcm.Length==4&&pcm[1]==1,"raw PCM returned without playback");
            try{await LocalSpeech.SynthesizePcmWithHelper("silent fixture",1m,CancellationToken.None,helper,"changed-voice");throw new Exception("expected voice mismatch");}
            catch(InvalidOperationException){count++;}
            foreach(decimal badRate in new[]{0.6m,0.8m,0.9m}){
                try{await LocalSpeech.SynthesizePcmWithHelper("silent fixture",badRate,CancellationToken.None,helper,null);throw new Exception("expected PCM failure");}
                catch(InvalidOperationException){count++;}
            }
            string path=Path.Combine(dir,"settings.json");
            var prefs=VoicePreferences.Load(path);Check(prefs.Provider=="system"&&prefs.Rate==10,"defaults");
            prefs.Provider="tencent";prefs.Rate=20;prefs.Save(path);
            prefs=VoicePreferences.Load(path);Check(prefs.Provider=="tencent"&&prefs.Rate==20,"roundtrip");
            prefs.VoiceMappings["tencent:category:天裔:female"]="501002";prefs.Save(path);
            Check(VoicePreferences.Load(path).VoiceMappings["tencent:category:天裔:female"]=="501002","voice mapping roundtrip");
            prefs.Provider="system";prefs.Rate=6;prefs.Save(path);
            Check(VoicePreferences.Load(path).Rate==6,"atomic replacement");
            File.WriteAllText(path,"{\"Provider\":\"bad\",\"Rate\":5}");
            Check(VoicePreferences.Load(path).Provider=="system"&&VoicePreferences.Load(path).Rate==10,"invalid stored settings default");
            File.WriteAllText(path,"invalid json");Check(VoicePreferences.Load(path).Rate==10,"corrupt settings default");
            prefs.Provider="unknown";await Reject(()=>{prefs.Save(path);return Task.CompletedTask;},"provider validation");
            prefs.Provider="system";prefs.Rate=21;await Reject(()=>{prefs.Save(path);return Task.CompletedTask;},"rate validation");
            await Reject(()=>LocalSpeech.Run(new string('a',4097),1m,CancellationToken.None,helper),"UTF8 length");
            await Reject(()=>LocalSpeech.Run(new string('\u4e2d',1366),1m,CancellationToken.None,helper),"multibyte length");
            await Reject(()=>LocalSpeech.Run("x",0.5m,CancellationToken.None,helper),"speech rate");
            await Reject(()=>LocalSpeech.Run(" ",1m,CancellationToken.None,helper),"empty text");
            await Reject(()=>LocalSpeech.Run("\ud800",1m,CancellationToken.None,helper),"invalid Unicode");
            await LocalSpeech.Run(new string('a',4096),1m,CancellationToken.None,helper);count++;
            await LocalSpeech.Run(new string('\u4e2d',1365)+"x",1m,CancellationToken.None,helper);count++;
            try{await LocalSpeech.Run("x",0.6m,CancellationToken.None,helper);throw new Exception("missing error");}
            catch(InvalidOperationException ex){Check(ex.Message.Contains("no-chinese-voice"),"helper error surfaced");}
            using(var cts=new CancellationTokenSource()){
                string pidPath=Path.Combine(dir,"pid");var task=LocalSpeech.SynthesizePcmWithHelper(pidPath,0.7m,cts.Token,helper,null);
                try {
                var timer=Stopwatch.StartNew();while(!File.Exists(pidPath)&&timer.ElapsedMilliseconds<5000)await Task.Delay(20);
                Check(File.Exists(pidPath),"fake helper started");int pid=Int32.Parse(File.ReadAllText(pidPath));
                cts.Cancel();try{await task;throw new Exception("missing cancellation");}catch(OperationCanceledException){count++;}
                bool gone=false;try{using(var child=Process.GetProcessById(pid))gone=child.HasExited;}catch(ArgumentException){gone=true;}
                Check(gone,"owned process exited before return");
                } finally {cts.Cancel();try{task.GetAwaiter().GetResult();}catch(OperationCanceledException){}}
            }
            using(var cts=new CancellationTokenSource()){cts.Cancel();try{await LocalSpeech.Run("x",1m,cts.Token,helper);throw new Exception("pre-cancel");}catch(OperationCanceledException){count++;}}
            string realHost=Path.Combine(Path.GetDirectoryName(helper),"LocalSpeechHost.exe");
            string realVoice=await LocalSpeech.GetVoiceIdentity(CancellationToken.None,realHost);
            byte[] realPcm=await LocalSpeech.SynthesizePcmWithHelper("系统语音测试。",1m,CancellationToken.None,realHost,realVoice);
            Check(realPcm.Length>3200 && (realPcm.Length&1)==0,"real Chinese voice emits PCM through redirected stdout");
            Check(Array.Exists(realPcm,b=>b!=0),"real synthesis contains non-silent samples");
            await HostReject(realHost,"1",new string('a',4097),"4096 UTF-8 bytes");
            await HostReject(realHost,"1"," ","Speech text is required");
            await HostReject(realHost,"3","","Invalid local speech rate");
            await HostReject(realHost,"1 --pcm definitely-uninstalled-voice","系统语音测试。","Selected local voice unavailable");
            Console.WriteLine("Local speech: "+count+" checks passed (no audible playback).");
        } finally{Directory.Delete(dir,true);}
    }
}
