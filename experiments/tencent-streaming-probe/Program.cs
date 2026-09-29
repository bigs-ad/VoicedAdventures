using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.WebSockets;
using System.ComponentModel;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuestVoiceStreaming {
    public sealed partial class ProbeWindow : Form {
        const string Npc="你在这里帮助我们的亡灵哨兵的时候，药剂师伦弗利尔派人来找过你。他没有告诉我详情，但他想和你谈谈你交给他的那些狼的心脏。";
        readonly TextBox app=new TextBox(), id=new TextBox(), key=new TextBox();
        readonly Button play=new Button(), stop=new Button(), game=new Button { Name="GameTest" };
        readonly Button saveCredentials=new Button(), clearCredentials=new Button();
        readonly CredentialStore store;
        readonly VoicedAdventures.Qwen.QwenStore qwenStore;
        readonly CheckBox consent=new CheckBox();
        readonly TextBox log=new TextBox();
        readonly TextBox dialogue=new TextBox { ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(246,248,249) };
        readonly Label status=new Label();
        readonly TrackBar rate=new TrackBar { Minimum=6,Maximum=20,Value=10,TickFrequency=2,SmallChange=1,LargeChange=2,Dock=DockStyle.Fill,AutoSize=false };
        readonly Label rateValue=new Label { Text="1.0 倍",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter };
        readonly Button resetRate=new Button { Text="↺",Dock=DockStyle.Fill,AccessibleName="恢复默认语速" };
        readonly ToolTip tips=new ToolTip();
        readonly NarrationBudget budget=new NarrationBudget();
        long attempts { get { return budget.Count; } }
        CancellationTokenSource active; StreamingAudio audio; ClientWebSocket socket; bool closing,receiverUnsafe;
        public ProbeWindow(bool loadSaved=true,CredentialStore credentialStore=null) {
            using(var stream=typeof(ProbeWindow).Assembly.GetManifestResourceStream("QuestVoice.ico"))
            using(var icon=new Icon(stream)) Icon=(Icon)icon.Clone();
            store=credentialStore??CredentialStore.ForCurrentUser();
            qwenStore=loadSaved&&credentialStore==null?VoicedAdventures.Qwen.QwenStore.Current():new VoicedAdventures.Qwen.QwenStore(store.PreferencesPath+".qwen");
            Text="冒险有声 · Voiced Adventures · 0.1.0"; ClientSize=new Size(780,780); MinimumSize=new Size(720,800);
            StartPosition=FormStartPosition.CenterScreen; Font=new Font("Microsoft YaHei UI",10); BackColor=Color.White;
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=2,RowCount=11 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            int[] heights={44,40,40,40,120,48,56,48,56,34}; foreach(int h in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); Controls.Add(layout);
            var header=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            var title=new Label { Text="冒险有声",Font=new Font(Font.FontFamily,18,FontStyle.Bold),Dock=DockStyle.Fill };
            header.Controls.Add(title,0,0);header.Controls.Add(provider,1,0);
            layout.Controls.Add(header,0,0); layout.SetColumnSpan(header,2);
            AddField(layout,"AppId",app,1); AddField(layout,"SecretId",id,2); AddField(layout,"SecretKey",key,3);
            app.UseSystemPasswordChar=true; id.UseSystemPasswordChar=true; key.UseSystemPasswordChar=true;
            dialogue.Text=Npc; layout.Controls.Add(dialogue,0,4); layout.SetColumnSpan(dialogue,2);
            consent.Text="同意按角色配置将台词发送到腾讯云或阿里云，并使用额度。"; consent.Dock=DockStyle.Fill;
            layout.Controls.Add(consent,0,5); layout.SetColumnSpan(consent,2);
            layout.Controls.Add(new Label { Text="朗读语速",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft },0,6);
            var speedPanel=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty };
            speedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); speedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,82)); speedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
            speedPanel.Controls.Add(rate,0,0); speedPanel.Controls.Add(rateValue,1,0); speedPanel.Controls.Add(resetRate,2,0); layout.Controls.Add(speedPanel,1,6);
            rate.AccessibleName="朗读语速"; tips.SetToolTip(rate,"0.6～2.0 倍，下次播报生效"); tips.SetToolTip(resetRate,"恢复默认语速 1.0 倍");
            rate.ValueChanged+=delegate { rateValue.Text=(rate.Value/10m).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" 倍"; };
            resetRate.Click+=delegate { rate.Value=10; };
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false };
            play.Text="试听样例"; game.Text="接入游戏"; stop.Text="停止"; play.Size=stop.Size=game.Size=new Size(110,36); stop.Enabled=false;
            game.BackColor=Color.FromArgb(19,113,100); game.ForeColor=Color.White; game.FlatStyle=FlatStyle.Flat;
            automatic.Size=new Size(126,36); buttons.Controls.Add(automatic); buttons.Controls.Add(play); buttons.Controls.Add(stop); layout.Controls.Add(buttons,0,7); layout.SetColumnSpan(buttons,2);
            saveCredentials.Text="保存设置"; clearCredentials.Text="清除凭据";
            saveCredentials.Size=clearCredentials.Size=new Size(110,36);
            buttons.Controls.Add(saveCredentials); buttons.Controls.Add(clearCredentials);
            tips.SetToolTip(saveCredentials,"加密保存三项凭据和勾选状态，下次打开自动恢复");
            tips.SetToolTip(clearCredentials,"删除本机保存的三项凭据并清空输入，不会撤销腾讯云密钥");
            saveCredentials.Click+=delegate { SaveCredentials(); }; clearCredentials.Click+=delegate { ClearCredentials(); };
            status.Text="系统语音无需密钥 · 游戏尚未连接"; status.Dock=DockStyle.Fill;
            layout.Controls.Add(status,0,8); layout.SetColumnSpan(status,2);
            quota.Dock=DockStyle.Fill;quota.ForeColor=Color.DimGray;
            layout.Controls.Add(quota,0,9); layout.SetColumnSpan(quota,2);
            log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill; log.BackColor=Color.White;
            layout.Controls.Add(log,0,10); layout.SetColumnSpan(log,2);
            play.Click+=async delegate { await Play(); };
            game.Click+=async delegate { await Play(true); };
            stop.Click+=delegate { StopPlayback(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                FlushCredentialEdits();
                closing=true;
                if(active!=null || listening!=null || voicePreviewRunning) { e.Cancel=true; Cancel(); status.Text="正在停止播放并清理游戏接收器……"; return; }
                app.Clear(); id.Clear(); key.Clear();
            };
            FormClosed+=delegate { tips.Dispose(); };
            if(loadSaved) {
                try {
                    var saved=store.Load();
                    if(saved!=null) { app.Text=saved.AppId; id.Text=saved.SecretId; key.Text=saved.SecretKey; consent.Checked=saved.Consent; status.Text="已载入本机设置 · 自动播报未开启"; }
                } catch { status.Text="无法读取已保存凭据，请重新填写并保存，或清除旧凭据。"; }
            }
            InitializeAutomatic();
            InitializePlaybackMode();
            InitializeProvider(loadSaved);
            InitializeSimpleInterface();
            bool production=loadSaved && credentialStore==null;
            bool cacheReady=InitializeSessionCache(production);
            InitializeAutoConnect(production && cacheReady);
            if(!cacheReady){string fault=status.Text;SetConnectionPaused(true);status.Text=fault;}
        }
        void SaveCredentials() {
            if(active!=null || listening!=null) return;
            if(!SaveVoicePreferences())return;
            if(SelectedProvider=="system" && !ValidCredentials()){status.Text="已保存系统语音和语速；无需云端密钥。";return;}
            try { store.Save(app.Text.Trim(),id.Text.Trim(),key.Text.Trim(),consent.Checked); status.Text="已保存凭据和勾选状态，下次打开自动恢复。"; }
            catch(ArgumentException) { status.Text="请先填写有效的 AppId、SecretId 和 SecretKey。"; }
            catch { status.Text="保存失败，凭据未确认保存。请检查本机配置目录权限。"; }
        }
        void ClearCredentials() {
            if(active!=null) return;
            saveTimer.Stop();suppressSave=true;
            try { store.Clear(); app.Clear(); id.Clear(); key.Clear(); consent.Checked=false;credentialsDirty=false; settingsStatus.Text="已清除本机凭据；腾讯云密钥仍有效。"; }
            catch { settingsStatus.Text="清除失败，请检查本机配置目录权限。"; }
            finally {suppressSave=false;}
        }
        void AddField(TableLayoutPanel table,string name,TextBox field,int row) {
            table.Controls.Add(new Label { Text=name,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft },0,row);
            field.Dock=DockStyle.Fill; field.MaxLength=256; table.Controls.Add(field,1,row);
        }
        void Record(string text) {
            if(closing||IsDisposed) return;
            if(InvokeRequired) { BeginInvoke(new Action<string>(Record),text); return; }
            log.AppendText(RedactLog(text)+Environment.NewLine);
        }
        void Cancel() { if(voicePreviewCancellation!=null)voicePreviewCancellation.Cancel();StopAutomatic(true);CancelAudio();if(!closing)status.Text="已停止 · 自动播报已关闭"; }
        void StopPlayback() {
            if(voicePreviewCancellation!=null)voicePreviewCancellation.Cancel();
            narrator.Stop();CancelAudio();
            if(listening!=null)narrator.Enable(true,true);
            status.Text=listening!=null?"已停止播放 · 保持游戏连接":"已停止播放";
            RefreshAutomaticControls();
        }
        void CancelAudio() { if(active!=null) active.Cancel(); if(socket!=null) socket.Abort(); if(audio!=null) audio.Stop(); }
        void CaptureProgress(string state) {
            if(IsDisposed || closing) return;
            if(InvokeRequired) { BeginInvoke(new Action<string>(CaptureProgress),state); return; }
            if(state.StartsWith("diagnostic:")) { Record(state.Substring(11)); return; }
            if(state=="validated") { status.Text="已收到本次台词 · 正在确认接收器退出"; return; }
            status.Text=state=="ready"?"已连接怀旧服 · 等待本次 NPC 文本（最长 180 秒）":"正在启动游戏接收器 · 等待 Windows 权限确认";
            if(state=="ready") Record("接收器就绪：打开短 NPC 对话后输入 /va play 一次。");
        }
        Func<string,CancellationToken,Task<byte[]>> diagnosticTencent;
        async Task Play(bool fromGame=false,AutoRequest automaticRequest=null,CancellationToken automaticToken=default(CancellationToken),bool preview=false) {
            bool auto=automaticRequest!=null && !preview; Exception automaticFailure=null;
            string requestProvider=automaticRequest!=null?automaticRequest.Provider:SelectedProvider;
            bool local=requestProvider=="system";
            if(requestProvider!="system" && requestProvider!="tencent" && requestProvider!="qwen")throw new ProbeException("unsupported-provider");
            if(cacheUnavailable){status.Text="缓存尚未安全初始化，已停止播放。";if(auto)throw new ProbeException("cache-unavailable");return;}
            if(active!=null || (!auto && !preview && listening!=null)){if(auto)throw new ProbeException("playback-busy");return;}
            if(fromGame && receiverUnsafe) { status.Text="上次接收器清理未确认，已暂停游戏接入。"; return; }
            if(fromGame && !local && !consent.Checked) { status.Text="请先确认云端发送与额度使用。"; if(auto)throw new ProbeException("cloud-consent-required");return; }
            long appNumber;
            if(fromGame && requestProvider=="tencent" && (!Int64.TryParse(app.Text.Trim(),out appNumber) || appNumber<=0 || String.IsNullOrWhiteSpace(id.Text) || String.IsNullOrWhiteSpace(key.Text))) {
                status.Text="请填写有效的 AppId、SecretId 和 SecretKey。"; if(auto)throw new ProbeException("cloud-credentials-required");return;
            }
            string url=null;
            decimal requestedRate=automaticRequest!=null?automaticRequest.Rate:rate.Value/10m;
            active=auto||preview?CancellationTokenSource.CreateLinkedTokenSource(automaticToken):new CancellationTokenSource();
            active.CancelAfter(TimeSpan.FromSeconds(fromGame?240:60));autoPlaying=auto;
            var ct=active.Token; var watch=new Stopwatch(); GameMessage captured=automaticRequest!=null?automaticRequest.Message:null;
            cacheWriteFailed=false;
            play.Enabled=game.Enabled=false; stop.Enabled=true; app.Enabled=id.Enabled=key.Enabled=false; consent.Enabled=false;
            saveCredentials.Enabled=clearCredentials.Enabled=false;
            if(!auto && !preview)dialogue.Text=fromGame?"":Npc;
            string segmentProgress=auto && captured.SegmentCount>0?"第 "+(captured.SegmentIndex+1)+" / "+captured.SegmentCount+" 段 · ":"";
            Record(auto?"--- 自动 NPC 播报 ---":fromGame?"--- 单次游戏接入 ---":"--- 试听样例 ---");
            if(segmentProgress.Length>0)Record(segmentProgress+"已收到本段文字");
            RefreshAutomaticControls();
            double firstAudioMs=-1;
            Action<string> mark=delegate(string eventName) {
                double elapsedMs=watch.Elapsed.TotalMilliseconds;
                if(eventName=="first-audio")firstAudioMs=elapsedMs;
                string label=eventName=="first-audio"?"收到首段音频":eventName=="playback-start"?"播放已启动（软件估计）":eventName=="stream-finished"?"整段合成完成":"连接已建立";
                Record(label+"："+watch.Elapsed.TotalMilliseconds.ToString("F0")+" ms");
                if(eventName=="playback-start" && firstAudioMs>=0)
                    Record("首段音频 → 开播预缓冲等待："+(elapsedMs-firstAudioMs).ToString("F0")+" ms");
                if(eventName=="playback-start")status.Text=segmentProgress+"正在朗读";
                if(eventName=="playback-start" && captured!=null)
                    Record("首个传输事件 → 播放启动（非实际听感）："+TimeSpan.FromTicks(DateTime.UtcNow.Ticks-captured.FirstEventUtcTicks).TotalMilliseconds.ToString("F0")+" ms");
            };
            try {
                if(fromGame) {
                    captured=await GameCapture.Receive(ct,CaptureProgress);
                    ct.ThrowIfCancellationRequested();
                    dialogue.Text=captured.Text;
                    Record("收到真实 NPC 台词："+captured.Text);
                    Record("校验通过 · 丢失事件 0 · 接收器已退出并清理");
                    Record("首个传输事件 → 解码："+TimeSpan.FromTicks(captured.DecodeUtcTicks-captured.FirstEventUtcTicks).TotalMilliseconds.ToString("F0")+" ms");
                    Record("解码 → 安全交付："+TimeSpan.FromTicks(DateTime.UtcNow.Ticks-captured.DecodeUtcTicks).TotalMilliseconds.ToString("F0")+" ms");
                }
                ct.ThrowIfCancellationRequested();
                string spokenText=(fromGame||auto||preview)?captured.Text:Npc;
                VoiceSnapshot selectedVoice=automaticRequest!=null?automaticRequest.Voice:null;
                if(selectedVoice==null){
                    if(local)installedVoices=await LocalSpeech.GetInstalledVoices(ct);
                    selectedVoice=ResolveSpeakerVoice(new AutoRequest {Provider=requestProvider,Message=captured});
                }
                if(String.IsNullOrEmpty(selectedVoice.Identity))throw new ProbeException("no-chinese-voice");
                bool qwen=requestProvider=="qwen";
                if(!local && qwen!=selectedVoice.IsQwen)throw new ProbeException("voice-provider-mismatch");
                decimal effectiveRate=requestedRate;
                if(qwen)active.CancelAfter(TimeSpan.FromSeconds(600));
                string voice=qwen?selectedVoice.QwenModel+":"+selectedVoice.CloudVoice:local?selectedVoice.Identity:selectedVoice.Identity+":neutral:100";
                Record("实际音色："+selectedVoice.Label+" · "+selectedVoice.Status);
                string cacheKey=SessionAudioCache.Key(qwen?"qwen":local?"system":"tencent",voice,requestedRate,spokenText);
                byte[] cached;
                if(ReadCachedAudio(cacheKey,out cached)) {
                    cacheHits++;status.Text=segmentProgress+"正在播放缓存 · 不调用接口";
                    await PlayPcm(cached,ct,qwen?24000:16000);Record("缓存命中 · 未调用语音接口");status.Text="缓存播放完成";return;
                }
                if(local) {
                    if(!auto && !preview)budget.Take();watch.Start();RefreshAutomaticControls();
                    status.Text=segmentProgress+"系统语音朗读中 · 不使用云端";
                    byte[] pcm=await LocalSpeech.SynthesizePcm(spokenText,requestedRate,ct,voice);
                    await PlayPcm(pcm,ct);ct.ThrowIfCancellationRequested();RememberAudio(cacheKey,pcm);
                    status.Text="系统语音播放完成";Record("系统语音完成 · 未调用云端接口");return;
                }
                if(!consent.Checked)throw new ProbeException("cloud-consent-required");
                if(qwen){
                    await PlayQwen(selectedVoice.CloudVoice,spokenText,effectiveRate,cacheKey,ct,watch,mark,selectedVoice.QwenModel);
                    status.Text="千问播报完成 · 等待下一段对话";return;
                }
                if(diagnosticTencent==null && !ValidCredentials())throw new ProbeException("cloud-credentials-required");
                using(var complete=new MemoryStream()){
                    var parts=SpeechText.Split(spokenText,180);
                    foreach(string part in parts){
                        ct.ThrowIfCancellationRequested();
                        if(diagnosticTencent!=null){
                            cloudCalls++;byte[] pcm=await diagnosticTencent(part,ct);ct.ThrowIfCancellationRequested();
                            await PlayPcm(pcm,ct,16000);ct.ThrowIfCancellationRequested();complete.Write(pcm,0,pcm.Length);continue;
                        }
                        url=Signing.Request(app.Text.Trim(),id.Text.Trim(),key.Text.Trim(),part,requestedRate,selectedVoice.Identity);
                        // Audio initialization happens before any billable network request.
                        audio=new StreamingAudio();socket=new ClientWebSocket();ct.ThrowIfCancellationRequested();
                        if(!auto && !preview)budget.Take();
                        active.CancelAfter(TimeSpan.FromSeconds(60));watch.Start();RefreshAutomaticControls();
                        cloudCalls++;status.Text=segmentProgress+"正在连接腾讯云 · 本次第 "+cloudCalls+" 次调用";
                        using(var network=CancellationTokenSource.CreateLinkedTokenSource(ct)){
                            network.CancelAfter(TimeSpan.FromSeconds(30));await socket.ConnectAsync(new Uri(url),network.Token);url=null;
                        }
                        mark("connected");
                        using(var recording=new RecordingAudioSink(audio)){
                            await StreamPump.Run(socket,recording,mark,ct);ct.ThrowIfCancellationRequested();
                            byte[] pcm=recording.ToArray();complete.Write(pcm,0,pcm.Length);
                        }
                        Record("缓冲不足次数："+audio.Underruns);
                        socket.Dispose();socket=null;audio.Dispose();audio=null;
                    }
                    ct.ThrowIfCancellationRequested();RememberAudio(cacheKey,complete.ToArray());
                }
                status.Text=auto?(automatic.Checked?"播报完成 · 自动播报已开启，等待下一段对话":"播报完成 · 自动播报已关闭"):fromGame?"游戏台词播放完成 · 本次接入已结束":"样例播放完成 · 请留意开始等待时间与是否断续";
            } catch(OperationCanceledException e) { automaticFailure=e;status.Text="已停止或等待超时；没有自动重试。"; }
              catch(InvalidDataException e) { automaticFailure=e;receiverUnsafe=e.Message.StartsWith("receiver-cleanup"); status.Text=GameError(e.Message); Record("接入未完成："+e.Message+"；没有调用语音接口。"); }
              catch(Win32Exception e) { automaticFailure=new ProbeException("system-failed");status.Text=e.NativeErrorCode==1223?"已取消 Windows 权限确认，未调用语音接口。":"无法启动游戏接收器，未调用语音接口。"; }
              catch(ProbeException e) {automaticFailure=e;status.Text=e.Message=="cache-read-failed"?"语音缓存读取失败，未重新调用接口。请检查磁盘及缓存目录权限。":e.Message=="cloud-consent-required"?"请先在设置中确认云端发送与额度使用。":e.Message=="cloud-credentials-required"?"请在设置中填写有效的云端凭据。":"播放未完成："+e.Message+"（没有自动重试）"; }
              catch(VoicedAdventures.Qwen.QwenFailure e) {automaticFailure=new ProbeException("qwen-failed");status.Text="千问播放未完成："+e.Message;Record(status.Text);}
              catch(IOException) {automaticFailure=new ProbeException("audio-storage-failed");SetConnectionPaused(true);status.Text="语音文件读写失败，已暂停；不会自动重新付费生成。请检查磁盘空间和文件权限。";Record(status.Text);}
              catch(UnauthorizedAccessException) {automaticFailure=new ProbeException("audio-storage-failed");SetConnectionPaused(true);status.Text="无法保存语音文件，已暂停；不会自动重新付费生成。请检查文件权限。";Record(status.Text);}
              catch(Exception) {automaticFailure=new ProbeException("speech-failed");status.Text=ct.IsCancellationRequested?"已停止或超时。":local?"系统语音播放失败，请检查 Windows 中文语音和输出设备。":"连接或播放失败，请检查凭据、网络及输出设备。"; }
            finally {
                url=null; if(socket!=null) { socket.Dispose(); socket=null; }
                if(audio!=null) { audio.Dispose(); audio=null; }
                active.Dispose(); active=null;autoPlaying=false;
                if(auto && autoFault!=null)status.Text=autoFault;
                if(cacheWriteFailed)status.Text+=" · 缓存保存失败，重播可能需要重新合成";
                if(!closing) RefreshAutomaticControls();
                else if(!IsDisposed) BeginInvoke(new Action(Close));
            }
            if(auto && automaticFailure!=null)throw automaticFailure;
        }
        static string GameError(string code) {
            if(code=="game-access-denied") return "游戏进程权限不足，尚未建立连接；不是音源或音量问题。";
            if(code=="game-not-running") return "请先进入指定的怀旧服角色，再点接入游戏。";
            if(code=="game-ambiguous") return "检测到多个怀旧服进程，请只保留本次测试的一个。";
            if(code=="addon-resources-missing") return "没有找到完整的 VoicedAdventures 插件资源。";
            if(code=="game-identity-changed") return "游戏已退出或进程改变，请重新接入。";
            if(code=="game-exited") return "游戏已退出，本次接入已结束，未调用语音接口。";
            if(code=="receiver-timeout") return "等待游戏文本超时，已停止接收；没有调用语音接口。";
            if(code=="frame-invalid" || code=="receiver-events-lost") return "游戏文本不完整或发生事件丢失，本次未播报。";
            if(code.StartsWith("receiver-cleanup")) return "接收器清理未确认；本次未播报，请暂停重试并反馈。";
            return "游戏文本接收未通过校验或已超时，未调用语音接口。";
        }
        [STAThread] static void Main(string[] args) {
            Application.SetHighDpiMode(HighDpiMode.SystemAware); Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==0 && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) {
                MessageBox.Show("请正常双击打开，不要以管理员身份运行。只有游戏接收器需要单独授权。","冒险有声"); return;
            }
            // Diagnostic modes must never read or modify the user's real credential store.
            var testStore=args.Length==0?null:new CredentialStore(Path.Combine(Path.GetTempPath(),"QuestVoice-ui-"+Guid.NewGuid().ToString("N")+".bin"));
            using(var form=new ProbeWindow(args.Length==0,testStore)) {
                if(args.Length==1 && args[0]=="--audio-check") {
                    SynchronizationContext.SetSynchronizationContext(null);
                    foreach(int sampleRate in new[]{16000,24000})using(var sink=new StreamingAudio(sampleRate)) using(var timeout=new CancellationTokenSource(3000)) {
                        if(sink.WaveFormat.SampleRate!=sampleRate)throw new Exception("Incorrect audio sample rate");
                        sink.Add(new byte[3200],3200); sink.Start(); sink.Drain(timeout.Token).GetAwaiter().GetResult();
                    }
                    return;
                }
                if(args.Length==1 && args[0]=="--ui-check") {
                    form.Show(); Application.DoEvents();
                    form.CheckLogPage();
                    string[] expectedTabs={"主页","音色","音源","日志"};
                    if(form.mainTabs.TabPages.Count!=expectedTabs.Length)throw new Exception("unexpected main tabs");
                    for(int i=0;i<expectedTabs.Length;i++)if(form.mainTabs.TabPages[i].Text!=expectedTabs[i])throw new Exception("incorrect main tab order");
                    if(form.mainTabs.DrawMode!=TabDrawMode.OwnerDrawFixed || form.mainTabs.SizeMode!=TabSizeMode.Fixed)throw new Exception("tab alignment not configured");
                    using(var screenshot=new Bitmap(form.Width,form.Height)){
                        form.DrawToBitmap(screenshot,new Rectangle(Point.Empty,form.Size));
                        screenshot.Save(Path.Combine(AppContext.BaseDirectory,"centered-tabs-ui.png"));
                    }
                    if(form.Controls.Find("PlaybackVolumeRow",true).Length!=0)throw new Exception("desktop volume control must be removed");
                    var originalSize=form.Size;var originalFont=form.Font;
                    foreach(float fontSize in new[]{10f,14f}) {
                        form.Font=new Font(originalFont.FontFamily,fontSize);
                        foreach(var windowSize in new[]{form.MinimumSize,new Size(785,1480),originalSize}) {
                            form.Size=windowSize;
                            Application.DoEvents();
                            if(!form.status.Parent.ClientRectangle.Contains(form.status.Bounds))throw new Exception("status row clipped");
                        }
                    }
                    form.Font=originalFont;form.Size=originalSize;Application.DoEvents();
                    form.active=new CancellationTokenSource();
                    foreach(int percent in new[]{0,9,100,200}){
                        form.ApplyGameControl("settings:system:10:interrupt:"+percent);
                        if(PlaybackGain.Percent!=percent || form.voicePreferences.PlaybackVolume!=percent || VoicePreferences.Load(testStore.PreferencesPath).PlaybackVolume!=percent || form.active.IsCancellationRequested)throw new Exception("game volume must update and persist without interrupting audio");
                    }
                    form.ApplyGameControl("settings:system:10");
                    if(PlaybackGain.Percent!=200)throw new Exception("legacy settings reset volume");
                    form.active.Dispose();form.active=null;
                    if(form.rate.Visible || form.provider.Visible || form.playbackMode.Visible || form.play.Visible)throw new Exception("desktop playback configuration still visible");
                    if(form.saveCredentials.Visible || form.automatic.Visible || form.app.Visible)throw new Exception("home still exposes manual setup controls");
                    form.OpenSettings();Application.DoEvents();
                    if(form.Icon==null || form.settingsWindow.FindForm()!=form || form.OwnedForms.Length!=0)throw new Exception("settings must stay in main window");
                    form.app.Text="123456";form.id.Text="fake-autosave-id";form.key.Text="fake-autosave-secret";
                    var saveWatch=Stopwatch.StartNew();while(saveWatch.ElapsedMilliseconds<1100){Application.DoEvents();Thread.Sleep(20);}
                    var autoSaved=testStore.Load();
                    if(autoSaved==null || autoSaved.SecretKey!="fake-autosave-secret" || autoSaved.Consent)throw new Exception("debounced encrypted save without manual action");
                    form.consent.Checked=true;
                    if(!testStore.Load().Consent)throw new Exception("consent autosave");
                    form.active=new CancellationTokenSource();
                    form.key.Text="";form.consent.Checked=false;
                    if(!form.active.IsCancellationRequested)throw new Exception("revocation must cancel actual playback even with system selected");
                    form.active.Dispose();form.active=null;
                    if(testStore.Load().Consent || testStore.Load().SecretKey!="fake-autosave-secret")throw new Exception("incomplete edit preserves key but persists consent revocation");
                    form.ClearCredentials();form.FlushCredentialEdits();
                    if(testStore.Load()!=null)throw new Exception("clear must not autosave old credentials");
                    if(form.SelectedProvider!="system" || form.app.Enabled || form.consent.Enabled)throw new Exception("local speech default without cloud fields");
                    form.rate.Value=12;form.SaveCredentials();
                    using(var localReopened=new ProbeWindow(true,testStore)) {
                        if(localReopened.SelectedProvider!="system" || localReopened.rate.Value!=12 || localReopened.app.Text.Length!=0 || localReopened.consent.Checked || localReopened.active!=null || localReopened.listening!=null)
                            throw new Exception("local preferences persist without credentials or connection");
                    }
                    form.rate.Value=10;
                    form.provider.SelectedIndex=1;
                    if(form.automatic.Visible) throw new Exception("manual connection switch exposed");
                    if(form.automatic.Checked || form.listening!=null || form.narrator.Enabled)throw new Exception("automatic startup must be off");
                    form.consent.Checked=false;
                    form.Play(true).GetAwaiter().GetResult();
                    if(form.attempts!=0 || form.active!=null || !form.status.Text.Contains("确认")) throw new Exception("game consent gate");
                    form.consent.Checked=true; form.Play(true).GetAwaiter().GetResult();
                    if(form.attempts!=0 || form.active!=null || !form.status.Text.Contains("填写")) throw new Exception("game credential gate");
                    form.consent.Checked=false;
                    if(form.rate.Value!=10 || form.rateValue.Text!="1.0 倍") throw new Exception("default rate");
                    form.active=new CancellationTokenSource();
                    form.rate.Value=20;
                    if(form.rateValue.Text!="2.0 倍" || form.active.IsCancellationRequested || form.attempts!=0) throw new Exception("rate interrupts or requests");
                    form.rate.Value=6;
                    if(form.rateValue.Text!="0.6 倍") throw new Exception("slow rate label");
                    form.rate.Value=10;
                    if(form.rate.Value!=10 || form.rateValue.Text!="1.0 倍" || form.active.IsCancellationRequested) throw new Exception("reset rate");
                    form.active.Dispose(); form.active=null;
                    form.Play().GetAwaiter().GetResult();
                    if(form.attempts!=0 || !form.status.Text.Contains("确认")) throw new Exception("consent gate");
                    form.consent.Checked=true; form.Play().GetAwaiter().GetResult();
                    if(form.attempts!=0 || !form.status.Text.Contains("填写")) throw new Exception("credential gate");
                    if(!form.key.UseSystemPasswordChar || !form.id.UseSystemPasswordChar) throw new Exception("masking");
                    form.ApplyGameControl("settings:system:12");
                    if(!form.gameSettingsReady || form.SelectedProvider!="tencent" || form.rate.Value!=12 || form.attempts!=0 || form.listening!=null)throw new Exception("game settings preserve source and do not start speech");
                    form.active=new CancellationTokenSource();
                    form.ApplyGameControl("settings:system:12");
                    if(form.active.IsCancellationRequested)throw new Exception("identical preamble interrupts speech");
                    form.ApplyGameControl("stop");
                    if(!form.narrator.Enabled || !form.active.IsCancellationRequested)throw new Exception("game stop keeps listener acceptance");
                    form.active.Dispose();form.active=null;
                    form.listening=new CancellationTokenSource();form.SetAutomaticCheck(true);
                    form.StopPlayback();
                    if(form.listening.IsCancellationRequested || !form.automatic.Checked || !form.narrator.Enabled)throw new Exception("desktop stop disconnects listener");
                    form.listening.Dispose();form.listening=null;form.SetAutomaticCheck(false);
                    form.narrator.Disable();form.provider.SelectedIndex=1;form.rate.Value=10;
                    for(int i=0;i<8;i++)form.budget.Take();form.RefreshAutomaticControls();
                    if(form.attempts!=8 || !form.play.Enabled || !form.automatic.Enabled || form.quota.Text.Contains("剩余"))throw new Exception("obsolete call cap");
                    try {
                        form.app.Text="123456"; form.id.Text="ui-fake-id"; form.key.Text="ui-fake-secret"; form.SaveCredentials();
                        using(var reopened=new ProbeWindow(true,testStore)) {
                            if(reopened.app.Text!="123456" || reopened.id.Text!="ui-fake-id" || reopened.key.Text!="ui-fake-secret" || !reopened.consent.Checked || reopened.attempts!=0 || reopened.active!=null || reopened.automatic.Checked || reopened.listening!=null) throw new Exception("auto-fill without requests");
                            reopened.consent.Checked=false; reopened.SaveCredentials();
                            using(var uncheckedWindow=new ProbeWindow(true,testStore)) {
                                if(uncheckedWindow.consent.Checked || uncheckedWindow.attempts!=0) throw new Exception("unchecked restored");
                            }
                            reopened.ClearCredentials();
                            if(testStore.Load()!=null || reopened.consent.Checked || reopened.app.Text.Length+reopened.id.Text.Length+reopened.key.Text.Length!=0) throw new Exception("clear UI");
                        }
                    } finally { testStore.Clear();File.Delete(testStore.PreferencesPath); }
                    try {form.SessionUiChecks();} finally {testStore.Clear();File.Delete(testStore.PreferencesPath);}
                    return;
                }
                if(args.Length==2 && (args[0]=="--render" || args[0]=="--render-small" || args[0]=="--render-settings" || args[0]=="--render-cloud-settings" || args[0]=="--render-qwen-settings" || args[0]=="--render-race-settings")) {
                    if(args[0]=="--render-small") form.Size=form.MinimumSize;
                    form.Show(); Application.DoEvents();
                    form.SeedSessionRender();Application.DoEvents();
                    Form surface=form;
                    if(args[0]=="--render-cloud-settings" || args[0]=="--render-race-settings")form.provider.SelectedIndex=1;
                    if(args[0]=="--render-qwen-settings")form.provider.SelectedIndex=2;
                    if(args[0]=="--render-settings" || args[0]=="--render-cloud-settings" || args[0]=="--render-qwen-settings" || args[0]=="--render-race-settings"){form.OpenSettings();Application.DoEvents();}
                    if(args[0]=="--render-race-settings"){form.OpenRaceVoices();Application.DoEvents();}
                    using(var bmp=new Bitmap(surface.Width,surface.Height)) { surface.DrawToBitmap(bmp,new Rectangle(Point.Empty,surface.Size)); bmp.Save(args[1]); }
                    return;
                }
                Application.Run(form);
            }
        }
    }
}
