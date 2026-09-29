using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuestVoiceStreaming {
    public sealed partial class ProbeWindow {
        readonly CheckBox pauseConnection=new CheckBox {Text="启用游戏配音",Size=new Size(174,36)};
        readonly Button settingsButton=new Button {Text="音源设置",Size=new Size(96,36)};
        readonly Label settingsStatus=new Label {Dock=DockStyle.Fill,ForeColor=Color.DimGray};
        readonly System.Windows.Forms.Timer saveTimer=new System.Windows.Forms.Timer {Interval=700};
        readonly TabControl mainTabs=new TabControl {Dock=DockStyle.Fill};
        void InitializeTabAlignment() {
            mainTabs.DrawMode=TabDrawMode.OwnerDrawFixed;
            mainTabs.SizeMode=TabSizeMode.Fixed;
            Action resize=delegate {
                Size text=TextRenderer.MeasureText("角色音色",mainTabs.Font);
                mainTabs.ItemSize=new Size(text.Width+24,text.Height+12);
            };
            mainTabs.FontChanged+=delegate {resize();};resize();
            mainTabs.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                if(e.Index<0 || e.Index>=mainTabs.TabPages.Count)return;
                TextRenderer.DrawText(e.Graphics,mainTabs.TabPages[e.Index].Text,mainTabs.Font,e.Bounds,
                    SystemColors.ControlText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
                if(mainTabs.Focused && e.Index==mainTabs.SelectedIndex){
                    var focus=e.Bounds;focus.Inflate(-3,-3);ControlPaint.DrawFocusRectangle(e.Graphics,focus);
                }
            };
        }
        readonly TabPage historyPage=new TabPage("主页") {BackColor=Color.White};
        readonly TabPage logPage=new TabPage("日志") {Name="LogPage",BackColor=Color.White,Padding=new Padding(12)};
        readonly Button copyLog=new Button {Name="CopyLog",Text="复制日志",AutoSize=true};
        readonly Button clearLog=new Button {Name="ClearLog",Text="清空日志",AutoSize=true};
        readonly Label logStatus=new Label {AutoSize=true,Text="",Margin=new Padding(12,8,0,0)};
        void InitializeLogPage() {
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};
            actions.Controls.Add(copyLog);actions.Controls.Add(clearLog);actions.Controls.Add(logStatus);
            layout.Controls.Add(actions,0,0);layout.Controls.Add(log,0,1);logPage.Controls.Add(layout);
            copyLog.Click+=delegate {
                if(log.TextLength==0){logStatus.Text="暂无日志";return;}
                try{Clipboard.SetText(RedactLog(log.Text));logStatus.Text="已复制";}
                catch(System.Runtime.InteropServices.ExternalException){logStatus.Text="剪贴板忙，请重试";}
            };
            clearLog.Click+=delegate {log.Clear();logStatus.Text="已清空";};
        }
        string RedactLog(string text) {
            foreach(string secret in new[]{key.Text,id.Text,qwenApiKey.Text})
                if(!String.IsNullOrEmpty(secret))text=text.Replace(secret,"[已隐藏]");
            return text;
        }
        void CheckLogPage() {
            var selected=mainTabs.SelectedTab;
            mainTabs.SelectedTab=logPage;Application.DoEvents();
            if(!logPage.Contains(log) || settingsWindow.Contains(log) || !log.ReadOnly)throw new Exception("log not isolated in read-only page");
            int history=narrator.Entries.Count;string saved=dialogue.Text;
            key.Text="dummy-log-secret";Record("test dummy-log-secret");
            if(log.Text.Contains("dummy-log-secret"))throw new Exception("log leaked key");
            key.Text="";
            clearLog.PerformClick();
            if(log.TextLength!=0 || narrator.Entries.Count!=history || dialogue.Text!=saved)throw new Exception("log clear affected dialogue");
            if(!log.Parent.ClientRectangle.Contains(log.Bounds))throw new Exception("log clipped");
            mainTabs.SelectedTab=selected;
        }
        TabPage settingsWindow;
        TableLayoutPanel settingsLayout;
        readonly TabPage raceVoiceWindow=new TabPage("音色") {BackColor=Color.White,Padding=new Padding(12)};
        DataGridView raceVoiceGrid;bool loadingRaceVoices;
        string raceVoiceProvider;
        bool voicePreviewRunning;
        System.Threading.CancellationTokenSource voicePreviewCancellation;
        readonly TextBox voicePreviewText=new TextBox {Name="VoicePreviewText",Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,MaxLength=300,Text="你好，朋友。前面的道路并不平静，带上你的武器，照顾好与你一起出发的伙伴。等你回来，我们再谈谈接下来的计划。",AccessibleName="试听文本"};
        readonly Button voicePreviewPlay=new Button {Name="VoicePreviewPlay",Text="▶ 试听当前音色",AutoSize=true};
        readonly Button voicePreviewStop=new Button {Name="VoicePreviewStop",Text="■",AutoSize=true,Enabled=false,AccessibleName="停止试听"};
        readonly Label voicePreviewStatus=new Label {AutoSize=true,MaximumSize=new Size(600,0),Text=""};
        AutoRequest SelectedVoicePreview(){
            var row=raceVoiceGrid.CurrentRow;if(row==null)throw new ArgumentException("请先选择一个角色音色。");
            string text=voicePreviewText.Text.Trim();if(text.Length==0)throw new ArgumentException("请先填写试听文本。");
            string category=(string)row.Tag;
            var message=new GameMessage {Text=text};
            string[] parts=category.Split(':');
            if(category.StartsWith("category:")){message.SpeakerRace=parts[1];message.SpeakerSex=parts[2];}
            else if(category.StartsWith("sex:"))message.SpeakerSex=parts[1];
            else if(category=="reserved:neutral")message.Source="book";
            else if(category.StartsWith("reserved:child:"))message.SpeakerSex=parts[2];
            var mappings=new System.Collections.Generic.Dictionary<string,string>(voicePreferences.VoiceMappings);
            string configured;
            if(category.StartsWith("reserved:child:") && mappings.TryGetValue(raceVoiceProvider+":"+category,out configured) && configured!="@none"){
                message.NpcId="preview";mappings[raceVoiceProvider+":npc:preview"]=configured;
            }
            var voice=SpeakerVoiceRouting.Resolve(raceVoiceProvider,message,mappings,installedVoices);
            return new AutoRequest {Provider=raceVoiceProvider,Message=message,Voice=voice,Rate=rate.Value/10m};
        }
        async System.Threading.Tasks.Task PreviewSelectedVoice(){
            if(voicePreviewRunning || closing || clearingSession)return;
            AutoRequest request;
            try{raceVoiceGrid.EndEdit();request=SelectedVoicePreview();}
            catch(ArgumentException e){voicePreviewStatus.Text=e.Message;return;}
            voicePreviewRunning=true;voicePreviewPlay.Enabled=false;voicePreviewStop.Enabled=true;
            var owner=new System.Threading.CancellationTokenSource();voicePreviewCancellation=owner;
            try{
                narrator.Stop();CancelAudio();
                var wait=System.Diagnostics.Stopwatch.StartNew();
                while(active!=null || !narrator.Work.IsCompleted){
                    if(wait.ElapsedMilliseconds>5000){voicePreviewStatus.Text="上一段播放尚未停止，请稍后再试。";return;}
                    await System.Threading.Tasks.Task.Delay(25,owner.Token);
                }
                owner.Token.ThrowIfCancellationRequested();
                voicePreviewStatus.Text="正在试听："+VoiceLabel(request.Voice.Identity);
                await Play(false,request,owner.Token,true);
                voicePreviewStatus.Text=status.Text;
            }catch(OperationCanceledException){voicePreviewStatus.Text="试听已停止";}
            finally{
                voicePreviewCancellation=null;owner.Dispose();voicePreviewRunning=false;
                if(!closing){voicePreviewPlay.Enabled=true;voicePreviewStop.Enabled=false;
                    if(listening!=null && !listening.IsCancellationRequested && automatic.Checked && !connection.Paused)narrator.Enable(true,true);
                    RefreshAutomaticControls();}
                else if(!IsDisposed)BeginInvoke(new Action(Close));
            }
        }
        void VoicePreviewUiChecks(){
            string sample=voicePreviewText.Text;int savedRate=rate.Value;string savedDialogue=dialogue.Text;
            var savedMappings=new System.Collections.Generic.Dictionary<string,string>(voicePreferences.VoiceMappings);
            var savedSize=Size;var savedFont=Font;
            try{
                foreach(float fontSize in new[]{10f,14f}){
                    Font=new Font(savedFont.FontFamily,fontSize);Size=MinimumSize;Application.DoEvents();
                    foreach(Control control in new Control[]{voicePreviewText,voicePreviewPlay,voicePreviewStop})
                        if(!control.Visible || !control.Parent.ClientRectangle.Contains(control.Bounds))throw new Exception("preview control clipped");
                    if(raceVoiceGrid.Height<100)throw new Exception("preview consumed role table");
                }
                Font=savedFont;Size=savedSize;Application.DoEvents();
                using(var previewImage=new Bitmap(Width,Height)){
                    DrawToBitmap(previewImage,new Rectangle(0,0,Width,Height));
                    previewImage.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"voice-preview-ui.png"));
                }
                voicePreviewText.Text="试听测试。";rate.Value=12;
                raceVoiceGrid.CurrentCell=raceVoiceGrid.Rows[1].Cells[0];
                var request=SelectedVoicePreview();
                if(request.Voice.Identity!="501002" || request.Rate!=1.2m || request.Message.Text!="试听测试。")throw new Exception("preview selected voice/text/rate");
                voicePreferences.VoiceMappings["tencent:category:人类:male"]="@general:female";
                raceVoiceGrid.CurrentCell=raceVoiceGrid.Rows[2].Cells[0];
                if(SelectedVoicePreview().Voice.Identity!="501002")throw new Exception("preview general fallback");
                voicePreferences.VoiceMappings["tencent:reserved:child:male"]="@general:female";
                foreach(DataGridViewRow row in raceVoiceGrid.Rows)if((string)row.Tag=="reserved:child:male")raceVoiceGrid.CurrentCell=row.Cells[0];
                if(SelectedVoicePreview().Voice.Identity!="501002")throw new Exception("preview reserved role fallback");
                raceVoiceGrid.CurrentCell=raceVoiceGrid.Rows[1].Cells[0];
                string cacheKey=SessionAudioCache.Key("tencent","501002:neutral:100",1.2m,voicePreviewText.Text);
                sessionCache.Commit(cacheKey,new byte[]{2,0,3,0});
                int played=0;long calls=cloudCalls;int history=narrator.Entries.Count;
                string preferences=System.IO.File.ReadAllText(store.PreferencesPath);
                listening=new System.Threading.CancellationTokenSource();
                diagnosticPlayback=(pcm,ct)=>{if(pcm[0]!=2)throw new Exception("preview wrong cache");played++;return System.Threading.Tasks.Task.CompletedTask;};
                PreviewSelectedVoice().GetAwaiter().GetResult();
                if(played!=1 || cloudCalls!=calls || narrator.Entries.Count!=history || listening.IsCancellationRequested || dialogue.Text!=savedDialogue || preferences!=System.IO.File.ReadAllText(store.PreferencesPath))throw new Exception("preview changed history/connection/preferences or used cloud");
                diagnosticPlayback=(pcm,ct)=>{voicePreviewStop.PerformClick();ct.ThrowIfCancellationRequested();return System.Threading.Tasks.Task.CompletedTask;};
                PreviewSelectedVoice().GetAwaiter().GetResult();
                if(active!=null || voicePreviewRunning || voicePreviewStop.Enabled || !voicePreviewPlay.Enabled)throw new Exception("preview stop cleanup");
                voicePreviewText.Text=" ";PreviewSelectedVoice().GetAwaiter().GetResult();
                if(!voicePreviewStatus.Text.Contains("填写"))throw new Exception("empty preview text");
            }finally{
                diagnosticPlayback=null;if(listening!=null){listening.Dispose();listening=null;}
                voicePreferences.VoiceMappings=savedMappings;voicePreviewText.Text=sample;rate.Value=savedRate;
                Font=savedFont;Size=savedSize;dialogue.Text=savedDialogue;
            }
        }
        const string FollowVoice="跟随通用声音";
        static string FollowLabel(string category){return category=="reserved:neutral"||category=="default"?"无":category.EndsWith(":female",StringComparison.Ordinal)?"通用女声":category.EndsWith(":male",StringComparison.Ordinal)?"通用男声":FollowVoice;}
        string VoiceLabel(string value){
            return value=="qwen:Ethan"?"Ethan（男声）":value=="qwen:Cherry"?"Cherry（女声）":value=="601008"?"爱小豪 (601008)":value=="501002"?"智菊 (501002)":value;
        }
        void OpenRaceVoices(){
            raceVoiceProvider=VoiceProvider;
            if(raceVoiceGrid==null){
                raceVoiceGrid=new DataGridView {Dock=DockStyle.Fill,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,
                    AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,
                    AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.CellSelect,
                    MultiSelect=false,EditMode=DataGridViewEditMode.EditOnEnter};
                raceVoiceGrid.RowTemplate.Height=34;
                raceVoiceGrid.Columns.Add(new DataGridViewTextBoxColumn {Name="Category",HeaderText="角色分类",ReadOnly=true,FillWeight=45,SortMode=DataGridViewColumnSortMode.NotSortable});
                raceVoiceGrid.Columns.Add(new DataGridViewComboBoxColumn {Name="Voice",HeaderText="音色",FillWeight=55,FlatStyle=FlatStyle.Standard,SortMode=DataGridViewColumnSortMode.NotSortable});
                raceVoiceGrid.CurrentCellDirtyStateChanged+=delegate {if(raceVoiceGrid.IsCurrentCellDirty)raceVoiceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
                raceVoiceGrid.CellValueChanged+=delegate(object sender,DataGridViewCellEventArgs e){
                    if(loadingRaceVoices || e.RowIndex<0 || e.ColumnIndex!=1)return;
                    var row=raceVoiceGrid.Rows[e.RowIndex];string key=raceVoiceProvider+":"+(string)row.Tag,value=Convert.ToString(row.Cells[1].Value);
                    if(value=="通用男声" || value=="通用女声")voicePreferences.VoiceMappings[key]=value=="通用男声"?"@general:male":"@general:female";
                    else if(value=="无" && FollowLabel((string)row.Tag)!="无")voicePreferences.VoiceMappings[key]="@none";
                    else if(value==FollowVoice || value=="未配置" || value=="无")voicePreferences.VoiceMappings.Remove(key);
                    else voicePreferences.VoiceMappings[key]=value==VoiceLabel("601008")?"601008":value==VoiceLabel("501002")?"501002":value==VoiceLabel("qwen:Ethan")?"qwen:Ethan":value==VoiceLabel("qwen:Cherry")?"qwen:Cherry":value;
                    SaveVoicePreferences();
                };
                var previewLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
                previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,84));
                previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var previewButtons=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,WrapContents=false};
                previewButtons.Controls.Add(voicePreviewPlay);previewButtons.Controls.Add(voicePreviewStop);
                previewLayout.Controls.Add(raceVoiceGrid,0,0);previewLayout.Controls.Add(voicePreviewText,0,1);
                previewLayout.Controls.Add(previewButtons,0,2);previewLayout.Controls.Add(voicePreviewStatus,0,3);
                raceVoiceWindow.Controls.Add(previewLayout);
                tips.SetToolTip(voicePreviewPlay,"试听所选音色；未命中缓存时使用当前平台额度");tips.SetToolTip(voicePreviewStop,"停止试听");
                voicePreviewPlay.Click+=async delegate {await PreviewSelectedVoice();};
                voicePreviewStop.Click+=delegate {if(voicePreviewCancellation!=null)voicePreviewCancellation.Cancel();CancelAudio();};
                status.TextChanged+=delegate {if(voicePreviewRunning)voicePreviewStatus.Text=status.Text;};
            }
            loadingRaceVoices=true;
            try {
                raceVoiceGrid.Rows.Clear();
                bool cloud=raceVoiceProvider!="system";
                raceVoiceGrid.Enabled=cloud;voicePreviewPlay.Enabled=cloud;voicePreviewText.Enabled=cloud;
                voicePreviewStatus.Text=cloud?"":"系统默认音色";
                if(!cloud){mainTabs.SelectedTab=raceVoiceWindow;return;}
                var choices=(DataGridViewComboBoxColumn)raceVoiceGrid.Columns[1];choices.Items.Clear();
                choices.Items.AddRange(FollowVoice,"通用男声","通用女声","无");
                if(raceVoiceProvider=="tencent")choices.Items.AddRange(VoiceLabel("601008"),VoiceLabel("501002"));
                else choices.Items.AddRange(VoiceLabel("qwen:Ethan"),VoiceLabel("qwen:Cherry"));
                var categories=new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>>();
                categories.Add(new System.Collections.Generic.KeyValuePair<string,string>("sex:male","通用 · 男"));
                categories.Add(new System.Collections.Generic.KeyValuePair<string,string>("sex:female","通用 · 女"));
                categories.AddRange(SpeakerVoiceRouting.Categories);
                foreach(var category in categories){
                    string value;voicePreferences.VoiceMappings.TryGetValue(raceVoiceProvider+":"+category.Key,out value);
                    bool general=category.Key.StartsWith("sex:",StringComparison.Ordinal);
                    if(general && String.IsNullOrWhiteSpace(value)){
                        if(raceVoiceProvider=="tencent")value=category.Key=="sex:female"?"501002":"601008";
                        else if(!voicePreferences.VoiceMappings.TryGetValue("qwen:"+category.Key,out value))value=category.Key=="sex:female"?voicePreferences.QwenGeneralFemaleVoice:voicePreferences.QwenGeneralMaleVoice;
                    }
                    string display=String.IsNullOrWhiteSpace(value)?FollowLabel(category.Key):value=="@general:male"?"通用男声":value=="@general:female"?"通用女声":value=="@none"?"无":VoiceLabel(value);
                    if(general && String.IsNullOrWhiteSpace(value))display="未配置";
                    if(!choices.Items.Contains(display))choices.Items.Add(display);
                    int index=raceVoiceGrid.Rows.Add(category.Value,display);var row=raceVoiceGrid.Rows[index];row.Tag=category.Key;
                    var cell=(DataGridViewComboBoxCell)row.Cells[1];
                    cell.Items.Remove(FollowVoice);
                    if(general)foreach(string follow in new[]{"通用男声","通用女声","无"})cell.Items.Remove(follow);
                    else row.Cells[1].ToolTipText="通用男声、通用女声跟随顶部配置；无表示不单独指定，沿用默认回退。";
                    if(category.Key.StartsWith("reserved:"))row.Cells[0].ToolTipText="仅保存预留配置，当前不会自动识别或套用。";
                    if(category.Key=="default")row.Cells[0].ToolTipText="仅在性别未知时使用；无表示未单独配置，仍使用当前平台的默认回退声音。";
                }
            }finally{loadingRaceVoices=false;}
            mainTabs.SelectedTab=raceVoiceWindow;
        }
        bool suppressSave,credentialsDirty,syncEnable,qwenKeyDirty;
        readonly TextBox qwenApiKey=new TextBox {UseSystemPasswordChar=true};
        void AddVoiceChoice(TableLayoutPanel settings,string title,string sex,int row){
            var choice=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,AccessibleName=title};
            choice.Items.AddRange(new object[]{"爱小豪 (601008)","智菊 (501002)"});
            string value;string mappingKey="tencent:sex:"+sex;
            if(voicePreferences.VoiceMappings==null)voicePreferences.VoiceMappings=new System.Collections.Generic.Dictionary<string,string>();
            if(!voicePreferences.VoiceMappings.TryGetValue(mappingKey,out value))value=sex=="female"?"501002":"601008";
            if(value!="601008" && value!="501002")choice.Items.Add(value);
            choice.SelectedIndex=value=="601008"?0:value=="501002"?1:2;
            choice.SelectedIndexChanged+=delegate {voicePreferences.VoiceMappings[mappingKey]=choice.SelectedIndex==0?"601008":choice.SelectedIndex==1?"501002":value;SaveVoicePreferences();};
            settings.Controls.Add(new Label {Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);settings.Controls.Add(choice,1,row);
        }
        void InitializeSimpleInterface() {
            var old=Controls[0];Controls.Remove(old);
            InitializeTabAlignment();
            settingsWindow=new TabPage("音源") {BackColor=Color.White};
            mainTabs.TabPages.Add(historyPage);mainTabs.TabPages.Add(raceVoiceWindow);mainTabs.TabPages.Add(settingsWindow);mainTabs.TabPages.Add(logPage);
            mainTabs.SelectedIndexChanged+=delegate {FlushCredentialEdits();};
            var settings=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=2,RowCount=11};settingsLayout=settings;
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in new[]{40,40,40,40,56,0,0,0,40,42})settings.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            settings.RowStyles.Add(new RowStyle(SizeType.Percent,100));settingsWindow.Controls.Add(settings);
            AddField(settings,"AppId",app,1);AddField(settings,"SecretId",id,2);AddField(settings,"SecretKey",key,3);
            settings.Controls.Add(consent,0,4);settings.SetColumnSpan(consent,2);
            var speed=rate.Parent;
            speed.Parent=null;speed.Visible=false;rate.Visible=false;
            settings.Controls.Add(new Label {Text="音源",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
            settings.Controls.Add(provider,1,0);provider.Visible=true;playbackMode.Visible=false;
            play.Parent=null;play.Visible=false;
            AddField(settings,"千问北京 Key",qwenApiKey,8);qwenApiKey.MaxLength=512;
            try{qwenApiKey.Text=qwenStore.LoadKey()??"";}catch{status.Text="千问凭据读取失败，请在音源设置重新填写。";}
            qwenApiKey.TextChanged+=delegate {if(suppressSave||closing)return;qwenKeyDirty=true;saveTimer.Stop();saveTimer.Start();};
            qwenApiKey.Leave+=delegate {FlushCredentialEdits();};
            settings.Controls.Add(settingsStatus,0,9);settings.SetColumnSpan(settingsStatus,2);
            InitializeLogPage();
            settingsStatus.Text="设置自动保存；凭据仅在本机加密保存。";
            var home=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=7};
            foreach(int height in new[]{48,48,36})home.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            home.RowStyles.Add(new RowStyle(SizeType.Percent,55));home.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            home.RowStyles.Add(new RowStyle(SizeType.Percent,45));home.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            var titleRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210));titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            titleRow.Controls.Add(new Label {Text="冒险有声",Font=new Font(Font.FontFamily,20,FontStyle.Bold),Dock=DockStyle.Fill},0,0);
            titleRow.Controls.Add(gameClient,1,0);home.Controls.Add(titleRow,0,0);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
            actions.Controls.Add(pauseConnection);actions.Controls.Add(stop);
            actions.Controls.Add(clearSession);
            home.Controls.Add(actions,0,1);
            PlaybackGain.Percent=voicePreferences.PlaybackVolume;
            home.Controls.Add(status,0,2);InitializeSessionList(home);historyPage.Controls.Add(home);Controls.Add(mainTabs);
            automatic.Parent=null;automatic.Visible=false;saveCredentials.Parent=null;saveCredentials.Visible=false;
            clearCredentials.Parent=null;clearCredentials.Visible=false;
            old.Dispose();dialogue.Clear();ClientSize=new Size(780,650);MinimumSize=new Size(700,580);
            Text="冒险有声 · Voiced Adventures · 0.1.0";stop.Text="停止朗读";
            pauseConnection.CheckedChanged+=delegate {if(!syncEnable)SetConnectionPaused(!pauseConnection.Checked);};
            settingsButton.Click+=delegate {OpenSettings();};
            saveTimer.Tick+=delegate {FlushCredentialEdits();};
            foreach(var field in new[]{app,id,key}) {
                field.TextChanged+=delegate {if(suppressSave||closing)return;credentialsDirty=true;settingsStatus.Text="等待保存……";saveTimer.Stop();saveTimer.Start();};
                field.Leave+=delegate {FlushCredentialEdits();};
            }
            consent.CheckedChanged+=delegate {
                if(suppressSave||closing)return;
                if(!consent.Checked)StopPlayback();
                credentialsDirty=true;FlushCredentialEdits();
            };
            FormClosed+=delegate {saveTimer.Dispose();settingsButton.Dispose();automatic.Dispose();saveCredentials.Dispose();speed.Dispose();provider.Dispose();playbackMode.Dispose();play.Dispose();};
            RefreshCloudRows();RefreshConnectionButton();
        }
        void OpenSettings() {
            if(closing || settingsWindow.IsDisposed)return;
            mainTabs.SelectedTab=settingsWindow;
        }
        void RefreshCloudRows() {
            if(settingsLayout==null)return;
            bool cloud=SelectedProvider!="system",qwen=cloud&&VoiceProvider=="qwen";
            consent.Text="同意将台词发送至"+(qwen?"阿里云":"腾讯云")+"，并使用语音额度。";
            settingsLayout.RowStyles[8].Height=qwen?40:0;
            foreach(Control control in settingsLayout.Controls)if(settingsLayout.GetRow(control)==8)control.Visible=qwen;
            if(raceVoiceGrid==null || raceVoiceProvider!=VoiceProvider){var selected=mainTabs.SelectedTab;OpenRaceVoices();mainTabs.SelectedTab=selected;}
            for(int row=1;row<=6;row++) {
                bool visible=row==4?cloud:row<4&&cloud&&VoiceProvider=="tencent";
                settingsLayout.RowStyles[row].Height=visible?(row==4?56:40):0;
                foreach(Control control in settingsLayout.Controls)if(settingsLayout.GetRow(control)==row)control.Visible=visible;
            }
            if(!cloud)settingsStatus.Text="系统语音 · 无需 API 凭据";
            else if(settingsStatus.Text=="系统语音 · 无需 API 凭据")settingsStatus.Text="设置自动保存；凭据仅在本机加密保存。";
        }
        void RefreshConnectionButton() {
            syncEnable=true;pauseConnection.Checked=!ConnectionPaused;syncEnable=false;
            pauseConnection.Enabled=!closing;
        }
        void FlushCredentialEdits() {
            saveTimer.Stop();if(suppressSave)return;
            if(qwenKeyDirty){
                try{if(String.IsNullOrWhiteSpace(qwenApiKey.Text)){settingsStatus.Text="千问 Key 未填完整，保留之前保存的凭据。";}else{qwenStore.SaveKey(qwenApiKey.Text.Trim());qwenKeyDirty=false;settingsStatus.Text="千问 Key 已在本机加密保存。";}}
                catch{settingsStatus.Text="千问 Key 保存失败，未覆盖原凭据。";}
            }
            if(!credentialsDirty)return;
            try {
                if(ValidCredentials()) {
                    store.Save(app.Text.Trim(),id.Text.Trim(),key.Text.Trim(),consent.Checked);
                    credentialsDirty=false;settingsStatus.Text="已自动保存到本机";
                } else {
                    // Incomplete replacement fields must not erase the last complete key.
                    var saved=store.Load();
                    if(saved!=null && saved.Consent!=consent.Checked)store.Save(saved.AppId,saved.SecretId,saved.SecretKey,consent.Checked);
                    settingsStatus.Text="凭据尚未填完整，保留之前保存的凭据。";
                }
            } catch {settingsStatus.Text="自动保存失败，请检查本机配置目录权限。";status.Text=settingsStatus.Text;}
        }
    }
}
