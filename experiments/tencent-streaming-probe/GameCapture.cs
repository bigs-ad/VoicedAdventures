using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.IO.Pipes;

namespace QuestVoiceStreaming {
    public sealed class GameTarget {
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        static string ProcessPath(Process process) {
            IntPtr handle=OpenProcess(0x1000,false,process.Id);
            if(handle==IntPtr.Zero)throw new InvalidDataException("game-access-denied");
            try {
                var name=new StringBuilder(32768);int length=name.Capacity;
                if(!QueryFullProcessImageName(handle,0,name,ref length))throw new InvalidDataException("game-access-denied");
                return name.ToString();
            } finally {CloseHandle(handle);}
        }
        public string Executable {get;private set;}
        public string Resources {get{return ResourcesFor(Executable);}}
        public static string ClientLabel(string executable) {
            ResourcesFor(executable);
            return UsesTurtleTransport(executable)?"水豚服":String.Equals(Path.GetFileName(executable),"WowB.exe",StringComparison.OrdinalIgnoreCase)?"无限服":"正式服";
        }
        public static bool UsesTurtleTransport(string executable) {
            return !String.IsNullOrEmpty(executable) && Path.IsPathFullyQualified(executable) &&
                String.Equals(Path.GetFileName(executable),"WoW.exe",StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(Path.GetFileName(Path.GetDirectoryName(executable)),"_retail_",StringComparison.OrdinalIgnoreCase);
        }
        public static string ResourcesFor(string executable) {
            if(!String.IsNullOrEmpty(executable) && Path.IsPathFullyQualified(executable) &&
                String.Equals(Path.GetFileName(executable),"WoW.exe",StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Path.GetFileName(Path.GetDirectoryName(executable)),"_retail_",StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Path.GetDirectoryName(executable),"Interface","AddOns","VoicedAdventures","Sounds");
            if(UsesTurtleTransport(executable)) {
                string addon=Path.Combine(Path.GetDirectoryName(executable),"Interface","AddOns","VoicedAdventures");
                string entry=Path.Combine(addon,"Main.lua");
                if(File.Exists(entry) && File.ReadAllText(entry).Contains("VA_TURTLE_VERSION = 1"))return Path.Combine(addon,"Sounds");
            }
            if(String.IsNullOrEmpty(executable) || !Path.IsPathFullyQualified(executable) ||
                !String.Equals(Path.GetFileName(executable),"WowB.exe",StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetFileName(Path.GetDirectoryName(executable)),"_classic_beta_",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("game-identity-changed");
            return Path.Combine(Path.GetDirectoryName(executable),"Interface","AddOns","VoicedAdventures","Sounds");
        }
        public int Pid; public long StartTicks;
        public static GameTarget FromDiscovery(GameMessage message,string session) {
            if(message==null || message.Kind!="target" || message.Version!=2 || message.Session!=session || message.Pid<=0 || message.StartTicks<=0)
                throw new InvalidDataException("game-discovery-invalid");
            ResourcesFor(message.Text);
            return new GameTarget {Pid=message.Pid,StartTicks=message.StartTicks,Executable=message.Text};
        }
        public static GameTarget Find() {
            var targets=FindAll();
            if(targets.Count==0)throw new InvalidDataException("game-not-running");
            if(targets.Count!=1)throw new InvalidDataException("game-ambiguous");
            return targets[0];
        }
        public string Identity {get{return Pid+":"+StartTicks;}}
        public static List<GameTarget> FindAll() {
            var results=new List<GameTarget>();
            foreach(string processName in new[]{"WowB","WoW"}) foreach(var process in Process.GetProcessesByName(processName)) using(process) {
                string path;
                try { path=ProcessPath(process); } catch { throw new InvalidDataException("game-access-denied"); }
                try {ResourcesFor(path);} catch(InvalidDataException) {continue;}
                var result=new GameTarget { Pid=process.Id,StartTicks=process.StartTime.ToUniversalTime().Ticks,Executable=path };
                for(int i=0;i<16;i++)if(!File.Exists(Path.Combine(result.Resources,"H"+i.ToString("X")+".wav")))throw new InvalidDataException("addon-resources-missing");
                results.Add(result);
            }
            return results;
        }
        public Process Open(bool game) {
            Process process=null;
            try {
                process=Process.GetProcessById(Pid);
                if(process.HasExited || process.StartTime.ToUniversalTime().Ticks!=StartTicks)
                    throw new InvalidDataException("game-identity-changed");
                if(game) {
                    string path=ProcessPath(process);ResourcesFor(path);
                    if(Executable!=null && !String.Equals(path,Executable,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("game-identity-changed");
                    Executable=path;
                }
                return process;
            } catch { if(process!=null) process.Dispose(); throw new InvalidDataException("game-identity-changed"); }
        }
    }
    public static class GameCapture {
        public static Task<GameMessage> Receive(CancellationToken ct,Action<string> progress,string mode="game") { return Run(ct,progress,mode,null); }
        public static Task<GameMessage> Listen(CancellationToken ct,Action<string> progress,Action<GameMessage> frame,string mode="auto-game") { return mode=="auto-game"?ListenAll(ct,progress,frame):Run(ct,progress,mode,frame); }
        static async Task<GameMessage> ListenAll(CancellationToken ct,Action<string> progress,Action<GameMessage> frame) {
            return await GameDiscovery.Connect(ct,discover=>Supervise(ct,frame,discover,(target,token)=>Run(token,progress,"auto-game",frame,target))).ConfigureAwait(false);
        }
        internal static async Task<GameMessage> Supervise(CancellationToken ct,Action<GameMessage> frame,Func<List<GameTarget>> discover,Func<GameTarget,CancellationToken,Task<GameMessage>> receive) {
            var running=new Dictionary<string,Task<GameMessage>>();
            var targets=new Dictionary<string,GameTarget>();
            var retired=new HashSet<string>();
            using(var owner=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                ExceptionDispatchInfo failure=null;
                try {
                    while(true) {
                        ct.ThrowIfCancellationRequested();
                        foreach(var pair in new List<KeyValuePair<string,Task<GameMessage>>>(running))if(pair.Value.IsCompleted) {
                            try {await pair.Value.ConfigureAwait(false);}
                            catch(InvalidDataException e){if(e.Message!="game-exited-clean" && e.Message!="game-not-running")throw;}
                            var gone=targets[pair.Key];
                            frame(new GameMessage {Kind="client-removed",Pid=gone.Pid,StartTicks=gone.StartTicks});
                            running.Remove(pair.Key);targets.Remove(pair.Key);retired.Add(pair.Key);
                        }
                        var found=discover();
                        var present=new HashSet<string>();foreach(var target in found)present.Add(target.Identity);
                        retired.RemoveWhere(key=>!present.Contains(key));
                        foreach(var target in found) {
                            if(running.ContainsKey(target.Identity)||retired.Contains(target.Identity))continue;
                            targets.Add(target.Identity,target);
                            frame(new GameMessage {Kind="client-added",Pid=target.Pid,StartTicks=target.StartTicks,Text=target.Executable});
                            running.Add(target.Identity,receive(target,owner.Token));
                        }
                        await Task.Delay(500,ct).ConfigureAwait(false);
                    }
                } catch(Exception e){failure=ExceptionDispatchInfo.Capture(e);}
                    owner.Cancel();
                    // Observe every helper's bounded cleanup before allowing a restart.
                    var pending=new List<Task<GameMessage>>(running.Values);
                    try {await Task.WhenAll(pending).ConfigureAwait(false);}catch {
                        foreach(var task in pending)if(task.Exception!=null)foreach(var error in task.Exception.Flatten().InnerExceptions)
                            if(error is InvalidDataException && error.Message.StartsWith("receiver-cleanup"))throw error;
                    }
                if(failure!=null)failure.Throw();
                return null;
            }
        }
        static async Task<GameMessage> Run(CancellationToken ct,Action<string> progress,string mode,Action<GameMessage> frame,GameTarget selected=null) {
            ct.ThrowIfCancellationRequested();
            bool automatic=mode.StartsWith("auto-");
            bool game=mode=="game" || mode=="auto-game";
            if(mode!="game" && mode!="offline-success" && mode!="offline-timeout" && mode!="auto-game" && mode!="auto-offline" && mode!="auto-timeout" && mode!="auto-corrupt" && mode!="auto-missing") throw new ArgumentException("mode");
            var self=Process.GetCurrentProcess();
            bool discover=false;
            GameTarget target;
            try { target=selected??(game?GameTarget.Find():new GameTarget { Pid=self.Id,StartTicks=self.StartTime.ToUniversalTime().Ticks }); }
            catch(InvalidDataException e) {
                if(mode!="auto-game" || e.Message!="game-access-denied")throw;
                discover=true;target=new GameTarget();
            }
            if(mode=="auto-game" && !discover && !File.ReadAllText(Path.Combine(Path.GetDirectoryName(target.Resources),"Main.lua")).Contains("QVR_AUTO_VERSION = 4")) throw new InvalidDataException("auto-addon-missing");
            string session=Guid.NewGuid().ToString("N"),name="QuestVoice-Game-"+session;
            string helper=Path.Combine(AppContext.BaseDirectory,"QuestVoiceReceiver.dll");
            if(!File.Exists(helper)) throw new InvalidDataException("receiver-missing");
            Process child=null;
            var pipe=GameWire.CreateServer(name);
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                if(mode!="auto-game")timeout.CancelAfter(TimeSpan.FromSeconds(automatic?915:180));
                GameMessage completed=null; ExceptionDispatchInfo error=null;
                try {
                    progress("elevation");
                    var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe"),
                        "\""+helper+"\" "+(discover?"auto-discover":mode)+" "+session+" "+self.Id+" "+self.StartTime.ToUniversalTime().Ticks+" "+target.Pid+" "+target.StartTicks) {
                        UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=AppContext.BaseDirectory
                    };
                    // UAC may wait for the user. Do not block the UI or start ETW before a verified pipe exists.
                    child=await Task.Run(()=>Process.Start(start)).ConfigureAwait(false);
                    if(child==null) throw new InvalidDataException("receiver-launch");
                    if(mode=="auto-game")timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    timeout.Token.ThrowIfCancellationRequested();
                    var exit=child.WaitForExitAsync();
                    var connect=pipe.WaitForConnectionAsync(timeout.Token);
                    if(await Task.WhenAny(connect,exit).ConfigureAwait(false)!=connect) throw new InvalidDataException("receiver-exited-before-ready");
                    await connect.ConfigureAwait(false);
                    GameWire.VerifyPeer(pipe,child.Id,true);
                    if(discover)target=GameTarget.FromDiscovery(await GameWire.Read(pipe,timeout.Token).ConfigureAwait(false),session);
                    var receipt=new GameReceipt(session,target.Pid,target.StartTicks);
                    var autoReceipt=automatic?new AutoReceipt(session,target.Pid,target.StartTicks):null;
                    GameMessage result=null;
                    while(true) {
                        var message=await GameWire.Read(pipe,timeout.Token).ConfigureAwait(false);
                        if(message==null) break;
                        if(message.Kind=="failure") progress("diagnostic:接收器报告：丢失事件 "+message.EventsLost+"；会话已清理 "+(message.Clean?"是":"未确认"));
                        var accepted=automatic?autoReceipt.Accept(message):receipt.Accept(message);
                        if(message.Kind=="ready") {if(mode=="auto-game")timeout.CancelAfter(Timeout.Infinite);progress("ready");}
                        if(accepted!=null) { result=accepted;if(automatic)frame(accepted);else progress("validated"); }
                    }
                    await exit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    if(child.ExitCode!=0 || (!automatic && result==null)) throw new InvalidDataException("receiver-incomplete");
                    timeout.Token.ThrowIfCancellationRequested();
                    using(target.Open(game)) { }
                    completed=result;
                } catch(Exception e) { error=ExceptionDispatchInfo.Capture(e); }
                finally { pipe.Dispose(); self.Dispose(); }
                if(child!=null) {
                    try {
                        using(var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                            await child.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                        bool cleanGameExit=mode=="auto-game" && error!=null && error.SourceException is InvalidDataException && error.SourceException.Message=="game-exited-clean";
                        if(cleanGameExit) {
                            try {using(var observed=Process.GetProcessById(target.Pid)){if(!observed.HasExited && observed.StartTime.ToUniversalTime().Ticks==target.StartTicks)cleanGameExit=false;}}
                            catch(ArgumentException){}catch{cleanGameExit=false;}
                        }
                        if(automatic && child.ExitCode==4 && !cleanGameExit)throw new InvalidDataException("auto-receiver-failed");
                        if(child.ExitCode!=0 && child.ExitCode!=1 && !(child.ExitCode==4 && cleanGameExit)) throw new InvalidDataException("receiver-cleanup-unconfirmed");
                    } catch(OperationCanceledException) { throw new InvalidDataException("receiver-cleanup-timeout"); }
                    finally { child.Dispose(); }
                }
                if(error!=null) error.Throw();
                ct.ThrowIfCancellationRequested();
                return completed;
            }
        }
    }
    public static class GameDiscovery {
        public static async Task<int> Serve(string session,int parentPid,long parentTicks) {
            using(var parent=new GameTarget {Pid=parentPid,StartTicks=parentTicks}.Open(false))
            using(var pipe=new NamedPipeClientStream(".","QuestVoice-Game-"+session,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                await pipe.ConnectAsync(5000).ConfigureAwait(false);GameWire.VerifyPeer(pipe,parentPid,false);
                long sequence=0;
                try {
                    while(!parent.HasExited) {
                        foreach(var target in GameTarget.FindAll())using(var timeout=new CancellationTokenSource(3000))
                            await GameWire.Write(pipe,new GameMessage {Version=2,Session=session,Kind="target",Sequence=++sequence,Pid=target.Pid,StartTicks=target.StartTicks,Text=target.Executable},timeout.Token).ConfigureAwait(false);
                        using(var timeout=new CancellationTokenSource(3000))await GameWire.Write(pipe,new GameMessage {Version=2,Session=session,Kind="scan-done",Sequence=++sequence},timeout.Token).ConfigureAwait(false);
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                }catch(IOException){}catch(OperationCanceledException){}
                return 0;
            }
        }
        public static async Task<GameMessage> Connect(CancellationToken ct,Func<Func<List<GameTarget>>,Task<GameMessage>> run) {
            ct.ThrowIfCancellationRequested();string session=Guid.NewGuid().ToString("N");
            Process child=null;GameMessage result=null;ExceptionDispatchInfo failure=null;Task reader=null;
            var gate=new object();var snapshot=new List<GameTarget>();
            using(var owner=CancellationTokenSource.CreateLinkedTokenSource(ct))
            using(var pipe=GameWire.CreateServer("QuestVoice-Game-"+session))
            using(var self=Process.GetCurrentProcess()) {
                try {
                    string helper=Path.Combine(AppContext.BaseDirectory,"QuestVoiceReceiver.dll");
                    var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe"),"\""+helper+"\" discover-games "+session+" "+self.Id+" "+self.StartTime.ToUniversalTime().Ticks+" 0 0") {UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=AppContext.BaseDirectory};
                    child=await Task.Run(()=>Process.Start(start)).ConfigureAwait(false);
                    using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct)) {deadline.CancelAfter(30000);await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);}
                    GameWire.VerifyPeer(pipe,child.Id,true);
                    reader=Task.Run(async delegate {
                        long sequence=0;var batch=new List<GameTarget>();
                        while(true) {
                            GameMessage message;
                            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(owner.Token)) {deadline.CancelAfter(5000);message=await GameWire.Read(pipe,deadline.Token).ConfigureAwait(false);}
                            if(message==null || message.Version!=2 || message.Session!=session || message.Sequence!=++sequence)throw new InvalidDataException("game-discovery-invalid");
                            if(message.Kind=="target") {
                                var target=GameTarget.FromDiscovery(message,session);
                                if(batch.Count>=16 || batch.Exists(t=>t.Identity==target.Identity))throw new InvalidDataException("game-discovery-invalid");
                                batch.Add(target);
                            } else if(message.Kind=="scan-done") {lock(gate)snapshot=batch;batch=new List<GameTarget>();}
                            else throw new InvalidDataException("game-discovery-invalid");
                        }
                    });
                    result=await run(delegate {
                        if(reader.IsCompleted)reader.GetAwaiter().GetResult();
                        lock(gate)return new List<GameTarget>(snapshot);
                    }).ConfigureAwait(false);
                }catch(Exception e){failure=ExceptionDispatchInfo.Capture(e);}
                owner.Cancel();pipe.Dispose();
                if(reader!=null)try{await reader.ConfigureAwait(false);}catch{}
                if(child!=null) {
                    try {using(var deadline=new CancellationTokenSource(10000))await child.WaitForExitAsync(deadline.Token).ConfigureAwait(false);}
                    catch(OperationCanceledException){throw new InvalidDataException("receiver-cleanup-timeout");}
                    finally{child.Dispose();}
                }
                if(failure!=null)failure.Throw();return result;
            }
        }
    }
}
