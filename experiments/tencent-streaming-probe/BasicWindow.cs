using System;
using System.Windows.Forms;

namespace QuestVoiceStreaming {
    public sealed partial class ProbeWindow {
        readonly ComboBox provider=new ComboBox {Name="SpeechProvider",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,AccessibleName="语音来源"};
        bool loadingPreferences,gameSettingsReady;
        VoicePreferences voicePreferences=new VoicePreferences();
        string VoiceProvider {get{return SelectedProvider;}}
        string SelectedProvider {get{return provider.SelectedIndex==2?"qwen":provider.SelectedIndex==1?"tencent":"system";}}
        void InitializeProvider(bool loadSaved) {
            loadingPreferences=true;
            provider.Items.AddRange(new object[]{"系统语音（免费，无需密钥）","腾讯云（在线）","阿里千问（在线，北京）"});
            provider.SelectedIndex=0;
            if(loadSaved) {
                try {var preferences=VoicePreferences.Load(store.PreferencesPath);voicePreferences=preferences;provider.SelectedIndex=preferences.Provider=="qwen"?2:preferences.Provider=="tencent"?1:0;rate.Value=preferences.Rate;playbackMode.SelectedIndex=preferences.QueueMode?1:0;}
                catch {status.Text="语音设置无法读取，已使用免费系统语音和默认语速。";}
            }
            loadingPreferences=false;
            provider.SelectedIndexChanged+=delegate {if(!loadingPreferences){if(narrator!=null){bool enabled=narrator.Enabled;narrator.Stop();CancelAudio();if(enabled)narrator.Enable(true,true);}SaveVoicePreferences();RefreshAutomaticControls();RefreshCloudRows();}};
            rate.ValueChanged+=delegate {if(!loadingPreferences)SaveVoicePreferences();};
            tips.SetToolTip(provider,"新对话和历史重播只使用所选音源。切换会取消旧音源待播队列；密钥仅在本机保存。");
            tips.SetToolTip(rate,"0.6～2.0，系统语音按近似语速档位调整；下次播报生效");
            RefreshAutomaticControls();
        }
        bool SaveVoicePreferences() {
            try {voicePreferences.Provider=SelectedProvider;voicePreferences.Rate=rate.Value;voicePreferences.QueueMode=playbackMode.SelectedIndex==1;voicePreferences.Save(store.PreferencesPath);return true;}
            catch {status.Text="语音设置保存失败，本次设置仍可使用。";return false;}
        }
        void ApplyGameControl(string text) {
            var command=GameControl.Parse(text);
            // Settings apply only to new entries; active and queued entries retain their snapshots.
            if(command.Stop) {
                narrator.Stop();CancelAudio();narrator.Enable(true,true);
            }
            if(!command.Stop) {
                gameSettingsReady=true;
                loadingPreferences=true;
                // Provider is desktop-owned; ignore the legacy provider wire field.
                rate.Value=command.Rate;playbackMode.SelectedIndex=command.QueueMode?1:0;
                if(command.Volume.HasValue){PlaybackGain.Percent=command.Volume.Value;voicePreferences.PlaybackVolume=command.Volume.Value;}
                loadingPreferences=false;SaveVoicePreferences();
            }
            status.Text=command.Stop?"已收到游戏停止指令 · 保持连接":"已收到游戏语音设置";
            RefreshAutomaticControls();RefreshCloudRows();
        }
    }
}
