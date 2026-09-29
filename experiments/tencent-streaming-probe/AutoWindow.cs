using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuestVoiceStreaming {
    public sealed partial class ProbeWindow {
        readonly CheckBox automatic=new CheckBox {Name="AutoPlay",Text="连接游戏",AccessibleName="连接游戏"};
        readonly Label quota=new Label();
        CancellationTokenSource listening;AutoNarration narrator;bool changingAuto,autoPlaying;string autoFault;
        readonly AutoConnectionState connection=new AutoConnectionState();
        readonly GameFocusState gameFocus=new GameFocusState();
        readonly System.Collections.Generic.Dictionary<string,string> gameNames=new System.Collections.Generic.Dictionary<string,string>();
        readonly ComboBox gameClient=new ComboBox {Name="GameClient",AccessibleName="当前游戏客户端",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        bool updatingClients;long clientDiscoveryAt;
        sealed class ClientOption {
            public string Key,Label;
            public override string ToString(){return Label;}
        }
        void RefreshClientChoices(){
            updatingClients=true;
            try {
                gameClient.Items.Clear();gameClient.Items.Add(new ClientOption {Label="请选择游戏客户端"});gameClient.SelectedIndex=0;
                foreach(var item in gameNames){var option=new ClientOption {Key=item.Key,Label=item.Value+" · PID "+item.Key.Split(':')[0]};gameClient.Items.Add(option);if(item.Key==gameFocus.Active)gameClient.SelectedItem=option;}
            } finally {updatingClients=false;}
        }
        static string ClientKey(GameMessage message){return message.Pid+":"+message.StartTicks;}
        void UpdateGameFocus() {
            if(clearingSession || voicePreviewRunning || connection.Paused)return;
            // Let the receiver publish its initial client inventory before single-client convenience selection.
            if(gameFocus.Active==null && gameNames.Count==1 && DateTime.UtcNow.Ticks-clientDiscoveryAt<TimeSpan.TicksPerMillisecond*500)return;
            if(!gameFocus.Select(0,DateTime.UtcNow.Ticks))return;
            GameSelectionChanged();
        }
        void GameSelectionChanged(){
            if(voicePreviewCancellation!=null)voicePreviewCancellation.Cancel();
            narrator.Stop();CancelAudio();narrator.Enable(true,true);gameSettingsReady=false;
            RefreshClientChoices();
            string settings;
            if(gameFocus.Active!=null && gameFocus.Settings.TryGetValue(gameFocus.Active,out settings))ApplyGameControl(settings);
            string name;
            status.Text=gameFocus.Active==null?"请选择要播报的游戏客户端":"当前播报："+(gameNames.TryGetValue(gameFocus.Active,out name)?name:"游戏")+" · 等待新对话";
        }
        bool RouteGameMessage(GameMessage message) {
            string key=ClientKey(message);
            if(message.Kind=="client-added"){clientDiscoveryAt=DateTime.UtcNow.Ticks;gameNames[key]=GameTarget.ClientLabel(message.Text);Record("已发现客户端："+gameNames[key]+" · PID "+message.Pid);gameFocus.Add(key,message.Pid);UpdateGameFocus();RefreshClientChoices();return false;}
            if(message.Kind=="client-removed"){gameFocus.Remove(key);gameNames.Remove(key);if(gameFocus.Select(0,DateTime.UtcNow.Ticks))GameSelectionChanged();RefreshClientChoices();return false;}
            string verifiedName;
            message.VerifiedRealm=gameNames.TryGetValue(key,out verifiedName)&&verifiedName=="无限服"?"unlimited":null;
            UpdateGameFocus();
            if(message.Kind=="control" && !GameControl.Parse(message.Text).Stop)gameFocus.Settings[key]=message.Text;
            return gameFocus.Accepts(key,message.FirstEventUtcTicks);
        }
        System.Windows.Forms.Timer connectionTimer;bool autoConnectEnabled;
        string[] installedVoices=new string[0];
        VoiceSnapshot ResolveSpeakerVoice(AutoRequest request){
            return SpeakerVoiceRouting.Resolve(request.Provider,request.Message,voicePreferences.VoiceMappings,installedVoices);
        }
        public bool ConnectionPaused {get{return connection.Paused;}}
        void InitializeAutoConnect(bool enabled) {
            autoConnectEnabled=enabled;
            if(!enabled)return;
            connectionTimer=new System.Windows.Forms.Timer {Interval=100};
            connectionTimer.Tick+=async delegate {if(listening!=null && !listening.IsCancellationRequested)UpdateGameFocus();await PollConnection();};
            FormClosed+=delegate {connectionTimer.Stop();connectionTimer.Dispose();};
            connectionTimer.Start();SetAutomaticCheck(true);
            status.Text="正在等待游戏 · 启动游戏后自动连接";RefreshAutomaticControls();
        }
        void SetConnectionPaused(bool paused) {
            connection.Pause(paused);SetAutomaticCheck(!paused);
            if(paused){if(narrator!=null)narrator.Stop();CancelAudio();if(listening!=null)listening.Cancel();status.Text="已暂停 · 点击恢复后自动连接游戏";}
            else status.Text="正在等待游戏 · 将自动连接";
            RefreshAutomaticControls();
        }
        async Task PollConnection() {
            if(!autoConnectEnabled || voicePreviewRunning || closing || clearingSession || receiverUnsafe || connection.Paused || connection.Busy || listening!=null || active!=null)return;
            try {if(GameTarget.FindAll().Count==0)return;}
            catch(InvalidDataException e){if(e.Message=="game-not-running")return;if(e.Message!="game-access-denied"){SetConnectionPaused(true);status.Text=GameError(e.Message);return;}}
            catch{SetConnectionPaused(true);status.Text="无法检查游戏状态，连接已暂停";return;}
            if(connection.TryBegin(true))await StartAutomatic();
        }
        void InitializeAutomatic() {
            narrator=new AutoNarration(budget,(request,ct)=>Play(false,request,ct),AutoState);
            narrator.ResolveVoice=ResolveSpeakerVoice;
            gameClient.SelectedIndexChanged+=delegate {
                if(updatingClients)return;
                var choice=gameClient.SelectedItem as ClientOption;
                if(choice!=null && gameFocus.Choose(choice.Key,DateTime.UtcNow.Ticks))GameSelectionChanged();
                else RefreshClientChoices();
            };
            RefreshClientChoices();
            automatic.CheckedChanged+=async delegate {
                if(changingAuto)return;
                SetConnectionPaused(!automatic.Checked);
                if(automatic.Checked)await PollConnection();
            };
            RefreshAutomaticControls();
        }
        bool ValidCredentials() {long number;return Int64.TryParse(app.Text.Trim(),out number)&&number>0&&!String.IsNullOrWhiteSpace(id.Text)&&!String.IsNullOrWhiteSpace(key.Text);}
        void SetAutomaticCheck(bool value) {changingAuto=true;automatic.Checked=value;changingAuto=false;}
        void RefreshAutomaticControls() {
            bool busy=active!=null || listening!=null;
            quota.Text="本次播报 "+attempts+" 段 · "+(SelectedProvider=="system"?"系统语音，不使用云端":SelectedProvider=="qwen"?"阿里千问":"腾讯云");
            play.Enabled=!busy;game.Enabled=play.Enabled&&!receiverUnsafe;
            stop.Enabled=busy;
            app.Enabled=id.Enabled=key.Enabled=active==null&&VoiceProvider=="tencent";
            consent.Enabled=(SelectedProvider!="system") || consent.Checked;
            provider.Enabled=active==null;
            saveCredentials.Enabled=clearCredentials.Enabled=active==null;
            automatic.Enabled=!closing&&!receiverUnsafe&&(automatic.Checked || (listening==null && (active==null||autoPlaying)));
            RefreshConnectionButton();
            RefreshReplayButton();
        }
        void StopAutomatic(bool stopSpeech) {
            connection.Pause(true);
            if(narrator!=null){if(stopSpeech)narrator.Stop();else narrator.Disable();}
            SetAutomaticCheck(false);
            if(listening!=null)listening.Cancel();
            RefreshAutomaticControls();
        }
        void AutoState(string state) {
            if(closing)return;
            if(state=="duplicate"){Record("已跳过正在排队、播放中或两分钟内已读完的相同台词，未重复调用。");return;}
            if(state=="narration-queue"){status.Text="待播队列已满，这条内容未加入播放。";Record(status.Text);return;}
            if(state=="speech-failed"){StopAutomatic(true);status.Text+=" · 游戏连接已暂停，没有自动重试";Record(status.Text);}
        }
        void PostAutomatic(CancellationTokenSource owner,Action action) {
            if(closing || IsDisposed)return;
            BeginInvoke(new Action(delegate {if(!closing && listening==owner && !owner.IsCancellationRequested)action();}));
        }
        async Task StartAutomatic() {
            if(!autoConnectEnabled || listening!=null){connection.Finish(false);return;}
            if(receiverUnsafe || (active!=null&&!autoPlaying)){connection.Finish(false);SetAutomaticCheck(false);status.Text="自动播报暂不可用，请先停止当前试听或检查接收器状态。";return;}
            if(!narrator.Enable(true,true)){connection.Finish(false);SetAutomaticCheck(false);return;}
            autoFault=null;gameSettingsReady=false;gameFocus.Clear();gameNames.Clear();RefreshClientChoices();
            var owner=new CancellationTokenSource();listening=owner;RefreshAutomaticControls();
            status.Text="正在开启自动播报 · 等待 Windows 权限确认";
            bool expectedStop=false,safeToWait=false;
            try {
                try {installedVoices=await LocalSpeech.GetInstalledVoices(owner.Token);}
                catch(OperationCanceledException){throw;}
                catch {installedVoices=new string[0];Record("本机中文声音读取失败；系统音源不切换云端。");}
                await GameCapture.Listen(owner.Token,delegate(string state) {
                    PostAutomatic(owner,delegate {
                        if(state=="ready") {status.Text="接收器已就绪 · 等待游戏对话";Record("接收器已就绪；游戏退出后会自动等待下次启动。");}
                        else if(state.StartsWith("diagnostic:"))Record(state.Substring(11));
                    });
                },delegate(GameMessage message) {
                    PostAutomatic(owner,delegate {
                        if(message.Kind=="metadata" || message.Kind=="control" || message.Kind=="rejected" || (message.Kind=="text" && message.SegmentIndex==0))
                            Record("游戏接收：PID "+message.Pid+" · "+message.Kind);
                        if(!RouteGameMessage(message))return;
                        if(voicePreviewRunning || clearingSession || message.FirstEventUtcTicks<=acceptAfterUtcTicks || !automatic.Checked || !narrator.Enabled)return;
                        if(message.Kind=="control"){ApplyGameControl(message.Text);return;}
                        if(message.Kind=="rejected") {
                            narrator.Stop();CancelAudio();narrator.Enable(true,true);
                            status.Text=message.Error=="too-long"?"这段台词超过 4096 字节保护上限，未截断、未发送这段台词。":"当前对话文字无法读取，已取消之前的播报。";
                            Record(status.Text);return;
                        }
                        if(!gameSettingsReady){status.Text="连接时错过了游戏设置，请点击游戏内播放按钮重播。";return;}
                        try {narrator.Accept(new AutoRequest {Message=message,Rate=rate.Value/10m,Provider=SelectedProvider});}
                        catch(ArgumentException){
                            StopAutomatic(true);status.Text="音色配置无效，已暂停播报。请在设置中重新选择音色。";Record(status.Text);
                        }
                    });
                });
                autoFault="游戏接收器已结束，连接已暂停；点击恢复后重新连接。";
                status.Text=autoFault;Record(autoFault);
            } catch(OperationCanceledException){expectedStop=owner.IsCancellationRequested;safeToWait=expectedStop;if(!expectedStop){autoFault="连接超时，已暂停；点击恢复后重新连接。";status.Text=autoFault;Record(autoFault);}}
              catch(Win32Exception e){expectedStop=true;status.Text=e.NativeErrorCode==1223?"已取消权限确认，自动播报未开启。":"接收器无法启动，自动播报未开启。";}
              catch(InvalidDataException e){safeToWait=e.Message=="game-exited-clean" || e.Message=="game-not-running";receiverUnsafe=e.Message.StartsWith("receiver-cleanup");autoFault=safeToWait?"游戏已退出 · 正在等待下次启动":e.Message=="auto-addon-missing"?"自动播报插件尚未安装，请完成插件更新。":e.Message.StartsWith("game-")?GameError(e.Message):"自动接收异常或超时，本轮接收未完整验证，已暂停，不会自动重试。";status.Text=autoFault;Record(autoFault+"（"+e.Message+"）");}
              catch{autoFault="自动接收器异常，本轮未完整验证，已停止播报；没有自动重试。";status.Text=autoFault;}
            finally {
                if(!expectedStop){narrator.Stop();CancelAudio();}
                narrator.Disable();gameFocus.Clear();gameNames.Clear();RefreshClientChoices();connection.Finish(safeToWait);SetAutomaticCheck(!connection.Paused);listening=null;owner.Dispose();
                if(!closing)RefreshAutomaticControls();
                else if(!IsDisposed)BeginInvoke(new Action(Close));
            }
        }
    }
}
