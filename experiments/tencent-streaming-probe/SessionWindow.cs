using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuestVoiceStreaming {
    sealed class HistoryListView : ListView {
        public HistoryListView(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Invalidate(true);}
    }
    public sealed partial class ProbeWindow {
        readonly ComboBox playbackMode=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        readonly ListView sessionList=new HistoryListView {Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false,HideSelection=false};
        readonly Button replay=new Button {Text="重新播放",Size=new Size(104,34),Enabled=false};
        readonly Button clearSession=new Button {Text="清空对话记录",Size=new Size(184,36)};
        bool clearingSession;long acceptAfterUtcTicks;
        readonly Label speakerDetails=new Label {Dock=DockStyle.Fill,AutoEllipsis=true};
        SessionAudioCache sessionCache;bool cacheUnavailable,listRefreshing,cacheWriteFailed;
        Func<string,CancellationToken,Task<byte[]>> diagnosticQwen;
        long cacheHits,cloudCalls;
        Func<byte[],CancellationToken,Task> diagnosticPlayback;
        void InitializePlaybackMode() {
            playbackMode.Items.AddRange(new object[]{"打断当前朗读","依次排队"});playbackMode.SelectedIndex=0;
            playbackMode.SelectedIndexChanged+=delegate {narrator.QueueMode=playbackMode.SelectedIndex==1;if(!loadingPreferences)SaveVoicePreferences();};
        }
        bool InitializeSessionCache(bool production) {
            try {
                string path=production?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QuestVoiceProbe","session-audio"):
                    Path.Combine(Path.GetTempPath(),"QuestVoice-session-test-"+Guid.NewGuid().ToString("N"));
                sessionCache=new SessionAudioCache(path,production);
                FormClosed+=delegate {if(sessionCache!=null){sessionCache.Dispose();sessionCache=null;}};
                return true;
            } catch {cacheUnavailable=true;status.Text="语音缓存清理或锁定失败，已暂停。请关闭其他助手并检查缓存目录权限。";return false;}
        }
        void InitializeSessionList(TableLayoutPanel home) {
            tips.SetToolTip(clearSession,"停止朗读并清空对话记录；保留语音缓存、凭据和设置。");
            clearSession.Click+=async delegate {await ClearSession();};
            sessionList.Columns.Add("说话人",140);sessionList.Columns.Add("任务 / 内容",350);sessionList.Columns.Add("状态",100);
            sessionList.Columns.Add("时间",90);
            sessionList.Resize+=delegate {
                int timeWidth=TextRenderer.MeasureText("00:00:00",sessionList.Font).Width+18;
                if(sessionList.Columns[3].Width!=timeWidth)sessionList.Columns[3].Width=timeWidth;
                int width=Math.Max(120,sessionList.ClientSize.Width-sessionList.Columns[0].Width-sessionList.Columns[2].Width-timeWidth-SystemInformation.VerticalScrollBarWidth-8);
                if(sessionList.Columns[1].Width!=width)sessionList.Columns[1].Width=width;
            };
            ResizeEnd+=delegate {sessionList.Invalidate(true);sessionList.Update();};
            home.Controls.Add(sessionList,0,3);
            var details=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));
            details.Controls.Add(speakerDetails,0,0);details.Controls.Add(replay,1,0);home.Controls.Add(details,0,4);
            home.Controls.Add(dialogue,0,5);home.Controls.Add(quota,0,6);
            narrator.Changed+=RefreshSessionList;
            sessionList.SelectedIndexChanged+=delegate {if(!listRefreshing)ShowSelectedEntry();};
            replay.Click+=delegate {
                if(clearingSession)return;
                var entry=SelectedEntry();if(entry==null || !entry.Complete)return;
                if(active!=null && !autoPlaying){status.Text="请先停止当前试听，再重播记录。";return;}
                try{var voice=ResolveSpeakerVoice(new AutoRequest {Provider=SelectedProvider,Message=entry.Message});narrator.Enable(true,true);narrator.Replay(entry,SelectedProvider,voice);}
                catch(ArgumentException){status.Text="请先为当前音源配置可用音色。";}
            };
        }
        NarrationEntry SelectedEntry(){return sessionList.SelectedItems.Count==0?null:sessionList.SelectedItems[0].Tag as NarrationEntry;}
        static string StateLabel(string state) {
            switch(state){case "queued":return "待播";case "playing":return "播放中";case "completed":return "已播";case "interrupted":return "已中断";case "failed":return "失败";default:return state;}
        }
        void RefreshSessionList() {
            if(closing||IsDisposed)return;
            var selected=SelectedEntry();listRefreshing=true;sessionList.BeginUpdate();
            try {
                sessionList.Items.Clear();
                foreach(var entry in narrator.Entries) {
                    var m=entry.Message;string name=String.IsNullOrEmpty(m.SpeakerName)?"未知说话人":m.SpeakerName;
                    string title=String.IsNullOrEmpty(m.QuestTitle)?entry.Text.Replace("\n"," "):m.QuestTitle;
                    var item=new ListViewItem(new[]{name,title,StateLabel(entry.State),entry.ReceivedAt.ToLocalTime().ToString("HH:mm:ss")}){Tag=entry};sessionList.Items.Add(item);
                    if(entry==selected)item.Selected=true;
                }
                if(sessionList.SelectedItems.Count==0 && sessionList.Items.Count>0)sessionList.Items[sessionList.Items.Count-1].Selected=true;
            } finally {sessionList.EndUpdate();listRefreshing=false;}
            ShowSelectedEntry();
        }
        void ShowSelectedEntry() {
            var entry=SelectedEntry();RefreshReplayButton();
            if(entry==null){speakerDetails.Text="";tips.SetToolTip(speakerDetails,"");dialogue.Clear();return;}
            var m=entry.Message;dialogue.Text=entry.Text;
            string sex=m.SpeakerSex=="male"?"男":m.SpeakerSex=="female"?"女":"性别未知";
            speakerDetails.Text=(String.IsNullOrEmpty(m.SpeakerName)?"未知说话人":m.SpeakerName)+" · "+
                (String.IsNullOrEmpty(m.SpeakerRace)?"种族未知":m.SpeakerRace)+" · "+sex+
                (String.IsNullOrEmpty(m.NpcId)?"":" · NPC "+m.NpcId)+
                (entry.Voice==null?"":" · "+entry.Voice.Label+" · "+entry.Voice.Status);
            tips.SetToolTip(speakerDetails,speakerDetails.Text);
        }
        async Task PlayPcm(byte[] pcm,CancellationToken ct,int sampleRate=16000) {
            if(diagnosticPlayback!=null){await diagnosticPlayback(pcm,ct);return;}
            ct.ThrowIfCancellationRequested();audio=new StreamingAudio(sampleRate);bool started=false;
            for(int offset=0;offset<pcm.Length;) {
                ct.ThrowIfCancellationRequested();
                while(audio.BufferedBytes>sampleRate*2*5)await Task.Delay(20,ct);
                int count=Math.Min(8192,pcm.Length-offset);var chunk=new byte[count];Array.Copy(pcm,offset,chunk,0,count);
                audio.Add(chunk,count);offset+=count;if(!started){audio.Start();started=true;}
            }
            await audio.Drain(ct);
        }
        void RefreshReplayButton(){var entry=SelectedEntry();replay.Enabled=!voicePreviewRunning && !clearingSession && entry!=null && entry.Complete && !cacheUnavailable && !(active!=null && !autoPlaying);clearSession.Enabled=!voicePreviewRunning && !clearingSession && !closing;}
        async Task ClearSession(){
            if(clearingSession || closing)return;
            bool resume=narrator.Enabled;clearingSession=true;RefreshReplayButton();
            try {
                narrator.Stop();CancelAudio();status.Text="正在停止朗读并清理记录……";
                using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10))){
                    await narrator.Work.WaitAsync(timeout.Token);
                    while(active!=null)await Task.Delay(25,timeout.Token);
                }
                if(closing)return;
                if(sessionCache==null)throw new IOException("Cache unavailable.");
                narrator.ClearHistory();cacheWriteFailed=false;
                acceptAfterUtcTicks=DateTime.UtcNow.Ticks;
                if(resume && !connection.Paused)narrator.Enable(true,true);
                log.Clear();
                status.Text="记录与日志已清空 · 会话语音缓存保留";
            }catch{
                if(!closing){SetConnectionPaused(true);status.Text="清理未完成，已暂停连接；请检查文件占用或权限后重试。";}
            }finally{
                clearingSession=false;if(!closing){RefreshSessionList();RefreshAutomaticControls();}
            }
        }
        bool ReadCachedAudio(string cacheKey,out byte[] pcm) {
            try {return sessionCache.TryRead(cacheKey,out pcm);}
            catch {throw new ProbeException("cache-read-failed");}
        }
        void SeedSessionRender() {
            narrator=new AutoNarration(budget,(r,ct)=>Task.CompletedTask,delegate{});narrator.Changed+=RefreshSessionList;narrator.Enable(true,true);
            for(int i=0;i<3;i++) {
                string text=i==0?Npc:"在我研究过阿鲁高的书以后，我最坏的设想不幸被证实了。\n\n我需要尽快研究一下这些物品。";
                var message=new GameMessage {DialogueId=(i+1).ToString("X24"),Text=text,SegmentCount=1,TotalBytes=System.Text.Encoding.UTF8.GetByteCount(text),TextChecksum=AutoSegments.Checksum(text),SpeakerName=i==0?"药剂师伦弗利尔":"达拉尔 · 道恩维沃尔",SpeakerRace="亡灵",SpeakerSex="male",NpcId="1938",QuestTitle=i==0?"给合格的货物":"阿鲁高的愚行",Source="npc"};
                narrator.Accept(new AutoRequest {Message=message,Provider="tencent",Rate=1m});narrator.Work.GetAwaiter().GetResult();
            }
            status.Text="播报完成 · 等待下一段对话";RefreshSessionList();
        }
        void SessionUiChecks() {
            provider.SelectedIndex=1;OpenRaceVoices();Application.DoEvents();
            if(raceVoiceWindow.Controls.Find("VoicePreviewText",true).Length!=1 || raceVoiceWindow.Controls.Find("VoicePreviewPlay",true).Length!=1 || raceVoiceWindow.Controls.Find("VoicePreviewStop",true).Length!=1)
                throw new Exception("role voice preview controls missing");
            VoicePreviewUiChecks();provider.SelectedIndex=0;mainTabs.SelectedTab=historyPage;
            mainTabs.SelectedTab=historyPage;Application.DoEvents();
            if(settingsWindow.Visible || !sessionList.Visible)throw new Exception("history tab visibility");
            OpenSettings();Application.DoEvents();
            if(!settingsWindow.Visible || sessionList.Visible || settingsWindow.FindForm()!=this || OwnedForms.Length!=0)throw new Exception("settings not embedded tab");
            mainTabs.SelectedTab=historyPage;OpenSettings();Application.DoEvents();
            if(!settingsWindow.Visible)throw new Exception("settings tab cannot reopen");
            ApplyGameControl("settings:system:10");provider.SelectedIndex=1;
            ApplyGameControl("settings:system:10");
            if(SelectedProvider!="tencent")throw new Exception("game overwrote desktop source");
            using(var reopened=new ProbeWindow(true,store)){
                reopened.ApplyGameControl("settings:system:10");
                if(reopened.SelectedProvider!="tencent")throw new Exception("desktop source not persisted across restart");
            }
            provider.SelectedIndex=0;
            ApplyGameControl("settings:system:10");
            if(SelectedProvider!="system")throw new Exception("desktop system provider not retained");
            ApplyGameControl("settings:tencent:10");
            if(SelectedProvider!="system")throw new Exception("legacy game source must be ignored");
            ApplyGameControl("settings:system:10");rate.Value=14;
            ApplyGameControl("settings:system:10");
            if(rate.Value!=10)throw new Exception("game must override obsolete desktop speed");
            ApplyGameControl("settings:system:16");
            if(rate.Value!=16)throw new Exception("changed game speed is ignored");
            active=new CancellationTokenSource();
            try {
                ApplyGameControl("settings:system:14");
                if(active.IsCancellationRequested)throw new Exception("changed game speed cancels frozen active narration");
                ApplyGameControl("stop");
                if(!active.IsCancellationRequested)throw new Exception("explicit stop does not cancel active narration");
            }finally{active.Dispose();active=null;}
            rate.Value=10;
            provider.SelectedIndex=0;RefreshCloudRows();
            if(app.Visible || id.Visible || key.Visible || consent.Visible || clearCredentials.Visible)throw new Exception("system UI exposes cloud fields or clearing");
            provider.SelectedIndex=1;RefreshCloudRows();
            if(!app.Visible || !consent.Visible)throw new Exception("cloud UI missing fields");
            foreach(var size in new[]{new Size(680,540),new Size(660,520)}){
                ClientSize=size;Application.DoEvents();
                if(settingsLayout.GetControlFromPosition(1,5)!=null || settingsLayout.GetControlFromPosition(1,6)!=null)throw new Exception("duplicate general voice controls");
            }
            OpenRaceVoices();Application.DoEvents();
            raceVoiceGrid.Rows[1].Cells[1].Value=VoiceLabel("601008");
            if(VoicePreferences.Load(store.PreferencesPath).VoiceMappings["tencent:sex:female"]!="601008")throw new Exception("voice selection not persisted");
            raceVoiceGrid.Rows[1].Cells[1].Value=VoiceLabel("501002");
            OpenRaceVoices();Application.DoEvents();
            if(raceVoiceGrid.Rows.Count!=26 || !raceVoiceWindow.Visible || (string)raceVoiceGrid.Rows[0].Tag!="sex:male" || (string)raceVoiceGrid.Rows[1].Tag!="sex:female")throw new Exception("general voices must precede race catalogue");
            var neutral=raceVoiceGrid.Rows[24];
            for(int rowIndex=2;rowIndex<raceVoiceGrid.Rows.Count;rowIndex++){
                var options=(DataGridViewComboBoxCell)raceVoiceGrid.Rows[rowIndex].Cells[1];
                if(!options.Items.Contains("通用男声")||!options.Items.Contains("通用女声")||!options.Items.Contains("无"))throw new Exception("role lacks general voice options");
            }
            raceVoiceGrid.Rows[2].Cells[1].Value="通用女声";
            if(VoicePreferences.Load(store.PreferencesPath).VoiceMappings["tencent:category:人类:male"]!="@general:female")throw new Exception("cross-sex alias not persisted");
            raceVoiceGrid.Rows[2].Cells[1].Value="无";
            if(VoicePreferences.Load(store.PreferencesPath).VoiceMappings["tencent:category:人类:male"]!="@none")throw new Exception("explicit none not persisted");
            raceVoiceGrid.Rows[2].Cells[1].Value="通用男声";
            if((string)neutral.Tag!="reserved:neutral" || Convert.ToString(neutral.Cells[1].Value)!="无")throw new Exception("neutral default must be none");
            neutral.Cells[1].Value=VoiceLabel("501002");
            if(VoicePreferences.Load(store.PreferencesPath).VoiceMappings["tencent:reserved:neutral"]!="501002")throw new Exception("neutral voice selection not saved");
            neutral.Cells[1].Value="无";
            if(VoicePreferences.Load(store.PreferencesPath).VoiceMappings.ContainsKey("tencent:reserved:neutral"))throw new Exception("neutral none not saved");
            foreach(string category in new[]{"category:亡灵:female","category:天裔:female","category:高等精灵:female"}){
                DataGridViewRow selected=null;
                foreach(DataGridViewRow row in raceVoiceGrid.Rows)if((string)row.Tag==category)selected=row;
                if(selected==null)throw new Exception("missing race category");
                selected.Cells[1].Value=VoiceLabel("601008");
                if(VoicePreferences.Load(store.PreferencesPath).VoiceMappings["tencent:"+category]!="601008")throw new Exception("race choice not persisted");
                OpenSettings();OpenRaceVoices();Application.DoEvents();
                foreach(DataGridViewRow row in raceVoiceGrid.Rows)if((string)row.Tag==category){
                    if(Convert.ToString(row.Cells[1].Value)!=VoiceLabel("601008"))throw new Exception("race choice lost on reopen");
                    row.Cells[1].Value=FollowLabel(category);
                }
                var savedFollow=VoicePreferences.Load(store.PreferencesPath).VoiceMappings;
                if(FollowLabel(category)=="无"){
                    if(savedFollow.ContainsKey("tencent:"+category))throw new Exception("none does not clear override");
                }else if(savedFollow["tencent:"+category]!=(category.EndsWith(":female")?"@general:female":"@general:male"))throw new Exception("follow alias not saved");
            }
            voicePreferences.VoiceMappings["tencent:default"]="1001";OpenRaceVoices();OpenRaceVoices();Application.DoEvents();
            if(Convert.ToString(raceVoiceGrid.Rows[25].Cells[1].Value)!="1001")throw new Exception("custom saved voice lost");
            raceVoiceGrid.Rows[25].Cells[1].Value="无";
            if(VoicePreferences.Load(store.PreferencesPath).VoiceMappings.ContainsKey("tencent:default"))throw new Exception("unknown none must clear explicit voice");
            OpenRaceVoices();
            if(Convert.ToString(raceVoiceGrid.Rows[25].Cells[1].Value)!="无")throw new Exception("unknown default label must be none after reload");
            Size=MinimumSize;Application.DoEvents();
            raceVoiceGrid.FirstDisplayedScrollingRowIndex=25;Application.DoEvents();
            if(!raceVoiceGrid.Rows[25].Displayed || raceVoiceGrid.Columns[1].Width<200)throw new Exception("race table not usable at minimum size");
            provider.SelectedIndex=0;Application.DoEvents();
            if(!mainTabs.TabPages.Contains(raceVoiceWindow) || raceVoiceGrid.Enabled || voicePreviewPlay.Enabled || raceVoiceGrid.Rows.Count!=0)throw new Exception("system source must retain voice tab without cloud settings");
            provider.SelectedIndex=1;OpenRaceVoices();mainTabs.SelectedTab=historyPage;Application.DoEvents();
            if(raceVoiceWindow.Visible || OwnedForms.Length!=0)throw new Exception("race settings not embedded tab");
            OpenSettings();
            provider.SelectedIndex=2;Application.DoEvents();
            if(provider.Items.Count!=3)throw new Exception("Exactly three speech providers required");
            if(app.Visible || id.Visible || key.Visible || !qwenApiKey.Visible || !consent.Visible)throw new Exception("Qwen credential isolation");
            OpenRaceVoices();Application.DoEvents();
            if(!((DataGridViewComboBoxColumn)raceVoiceGrid.Columns[1]).Items.Contains(VoiceLabel("qwen:Cherry")))throw new Exception("Built-in Qwen female voice missing");
            raceVoiceGrid.Rows[1].Cells[1].Value=VoiceLabel("qwen:Cherry");
            if(SelectedProvider!="qwen" || VoicePreferences.Load(store.PreferencesPath).Provider!="qwen")throw new Exception("Qwen provider persistence");
            OpenSettings();qwenApiKey.Text="dummy-qwen-key";FlushCredentialEdits();
            if(qwenStore.LoadKey()!="dummy-qwen-key"||!qwenApiKey.UseSystemPasswordChar)throw new Exception("Qwen encrypted autosave");
            provider.SelectedIndex=0;Application.DoEvents();
            if(qwenApiKey.Visible)throw new Exception("system source exposes Qwen key");
            provider.SelectedIndex=1;Application.DoEvents();
            if(qwenApiKey.Visible || !app.Visible || !key.Visible)throw new Exception("Tencent API fields isolation");
            ApplyGameControl("settings:tencent:10:queue");
            if(!VoicePreferences.Load(store.PreferencesPath).QueueMode || !narrator.QueueMode)throw new Exception("queue mode persistence");
            ApplyGameControl("settings:tencent:10:interrupt");
            if(narrator.QueueMode || !provider.Visible || rate.Visible || playbackMode.Visible || play.Visible)throw new Exception("source in assistant; speed and mode in game");
            var original=narrator;
            mainTabs.SelectedTab=historyPage;
            narrator=new AutoNarration(budget,(r,ct)=>Task.CompletedTask,delegate{});narrator.Changed+=RefreshSessionList;narrator.Enable(true,true);
            string text="这是一条测试任务。";
            var message=new GameMessage {DialogueId="010000000000000000000001",Text=text,SegmentCount=1,TotalBytes=System.Text.Encoding.UTF8.GetByteCount(text),TextChecksum=AutoSegments.Checksum(text),SpeakerName="测试说话人",SpeakerRace="未知",SpeakerSex="male",NpcId="123",QuestTitle="测试任务",Source="npc"};
            narrator.Accept(new AutoRequest {Message=message,Provider="tencent",Rate=1m});narrator.Work.GetAwaiter().GetResult();
            Application.DoEvents();RefreshSessionList();
            if(sessionList.Items.Count!=1 || dialogue.Text!=text || !speakerDetails.Text.Contains("123") || !replay.Enabled)throw new Exception("history text speaker selection");
            var received=narrator.Entries[0].ReceivedAt;
            if(sessionList.Columns.Count!=4 || sessionList.Columns[3].Text!="时间" || sessionList.Items[0].SubItems[3].Text!=received.ToLocalTime().ToString("HH:mm:ss"))throw new Exception("history timestamp column");
            for(int resize=0;resize<6;resize++){
                Size=resize%2==0?new Size(960,1000):MinimumSize;Application.DoEvents();
                OnResizeEnd(EventArgs.Empty);Application.DoEvents();
                if(sessionList.Items.Count!=1 || SelectedEntry()!=narrator.Entries[0] || sessionList.Items[0].SubItems[3].Text!=received.ToLocalTime().ToString("HH:mm:ss"))throw new Exception("resize loses history selection or timestamp");
            }
            active=new CancellationTokenSource();autoPlaying=false;RefreshAutomaticControls();
            if(replay.Enabled)throw new Exception("preview permits silent history replay");active.Dispose();active=null;
            narrator.Replay(narrator.Entries[0]);narrator.Work.GetAwaiter().GetResult();
            if(narrator.Entries.Count!=2)throw new Exception("manual history replay");
            ClearCredentials();provider.SelectedIndex=0;
            string cacheKey=SessionAudioCache.Key("tencent","601008:neutral:100",1m,text);sessionCache.Commit(cacheKey,new byte[]{0,0,1,0});
            sessionCache.Commit(SessionAudioCache.Key("tencent","501002:neutral:100",1m,text),new byte[]{2,0,3,0});
            int plays=0;diagnosticPlayback=(pcm,ct)=>{if(pcm.Length!=4)throw new Exception("cache PCM changed");plays++;return Task.CompletedTask;};
            long before=cloudCalls;
            try {
                Play(false,new AutoRequest {Message=message,Provider="tencent",Rate=1m}).GetAwaiter().GetResult();
                Play(false,new AutoRequest {Message=message,Provider="tencent",Rate=1m}).GetAwaiter().GetResult();
                if(plays!=2 || cloudCalls!=before || cacheHits<2)throw new Exception("cached game replay touched cloud or did not play");
                diagnosticPlayback=(pcm,ct)=>{if(pcm[0]!=2)throw new Exception("female playback reused male cached PCM");plays++;return Task.CompletedTask;};
                Play(false,new AutoRequest {Message=message,Provider="tencent",Rate=1m,Voice=new VoiceSnapshot("501002","智菊","test")}).GetAwaiter().GetResult();
                if(plays!=3 || cloudCalls!=before)throw new Exception("female snapshot cache playback touched cloud");
                string qwenCacheKey=SessionAudioCache.Key("qwen",VoicedAdventures.Qwen.Protocol.Model+":Ethan",1.2m,text);
                sessionCache.Commit(qwenCacheKey,new byte[]{8,0,9,0});
                diagnosticPlayback=(pcm,ct)=>{if(pcm[0]!=8)throw new Exception("Qwen reused Tencent cache");plays++;return Task.CompletedTask;};
                Play(false,new AutoRequest {Message=message,Provider="qwen",Rate=1.2m,Voice=new VoiceSnapshot("qwen:Ethan","Ethan","test")}).GetAwaiter().GetResult();
                if(plays!=4||cloudCalls!=before)throw new Exception("Qwen cache replay touched cloud or credentials");
                bool mismatch=false;
                try{Play(false,new AutoRequest {Message=message,Provider="tencent",Rate=1.2m,Voice=new VoiceSnapshot("qwen:Ethan","Ethan","test")}).GetAwaiter().GetResult();}catch(ProbeException){mismatch=true;}
                if(!mismatch || cloudCalls!=before || plays!=4)throw new Exception("provider mismatch reached playback or cloud");
                string preferencesBefore=File.ReadAllText(store.PreferencesPath);
                Record("old-settings-log-test");
                ClearSession().GetAwaiter().GetResult();byte[] cleared;
                if(log.TextLength!=0)throw new Exception("clear left settings log text");
                if(sessionList.Items.Count!=0 || dialogue.Text!="" || replay.Enabled || narrator.Entries.Count!=0)
                    throw new Exception("clear left visible session history");
                if(!sessionCache.TryRead(cacheKey,out cleared) || !sessionCache.TryRead(SessionAudioCache.Key("tencent","501002:neutral:100",1m,text),out cleared))
                    throw new Exception("clear deleted reusable audio");
                if(File.ReadAllText(store.PreferencesPath)!=preferencesBefore || cloudCalls!=before)
                    throw new Exception("clear changed settings or called cloud");
                narrator.Accept(new AutoRequest {Message=message,Provider="tencent",Rate=1m});narrator.Work.GetAwaiter().GetResult();
                if(narrator.Entries.Count!=1)throw new Exception("clear did not permit identical dialogue again");
                Size originalSize=Size;Size=MinimumSize;PerformLayout();Application.DoEvents();
                if(!clearSession.Visible || clearSession.Right>clearSession.Parent.ClientSize.Width || clearSession.Bottom>clearSession.Parent.ClientSize.Height)
                    throw new Exception("clear button clipped at minimum window size");
                Size=originalSize;
                cacheUnavailable=true;RefreshReplayButton();
                if(replay.Enabled)throw new Exception("Unavailable cache permits replay");
                cacheUnavailable=false;provider.SelectedIndex=0;
            }finally{diagnosticPlayback=null;narrator=original;RefreshSessionList();}
        }
        void RememberAudio(string cacheKey,byte[] pcm) {
            try {sessionCache.Commit(cacheKey,pcm);}
            catch {cacheWriteFailed=true;Record("语音已播放，但缓存保存失败；下次播放可能需要重新合成。");}
        }
        protected override void Dispose(bool disposing) {
            if(disposing) {
                saveTimer.Dispose();
                if(connectionTimer!=null)connectionTimer.Dispose();
                if(settingsWindow!=null)settingsWindow.Dispose();
                if(raceVoiceWindow!=null)raceVoiceWindow.Dispose();
                if(sessionCache!=null){sessionCache.Dispose();sessionCache=null;}
            }
            base.Dispose(disposing);
        }
    }
}
