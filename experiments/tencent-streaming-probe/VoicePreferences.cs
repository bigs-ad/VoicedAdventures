using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace QuestVoiceStreaming {
    public sealed class VoicePreferences {
        public string Provider {get;set;}
        public int Rate {get;set;}
        public int PlaybackVolume {get;set;}
        public bool QueueMode {get;set;}
        public bool DesktopProviderOverride {get;set;}
        public string LastGameProvider {get;set;}
        public Dictionary<string,string> VoiceMappings {get;set;}
        public string QwenGeneralMaleVoice {get;set;}
        public string QwenGeneralFemaleVoice {get;set;}
        public VoicePreferences(){Provider="system";Rate=10;PlaybackVolume=150;VoiceMappings=new Dictionary<string,string>();QwenGeneralMaleVoice="qwen:Ethan";QwenGeneralFemaleVoice="qwen:Cherry";SplitSources();}
        bool Valid(){return (Provider=="system"||Provider=="tencent"||Provider=="qwen")&&Rate>=6&&Rate<=20&&(LastGameProvider==null||LastGameProvider=="system"||LastGameProvider=="tencent"||LastGameProvider=="qwen");}
        public void SplitSources(){
            QwenGeneralMaleVoice="qwen:Ethan";QwenGeneralFemaleVoice="qwen:Cherry";
            if(VoiceMappings==null)VoiceMappings=new Dictionary<string,string>();
            foreach(var item in new List<KeyValuePair<string,string>>(VoiceMappings)){
                if(item.Value!=null && (item.Value.StartsWith("qwen-vc:",StringComparison.Ordinal) || item.Value.StartsWith("qwen-vd:",StringComparison.Ordinal))){VoiceMappings.Remove(item.Key);continue;}
                if(item.Key.StartsWith("qwen:",StringComparison.Ordinal) && item.Value!="qwen:Ethan" && item.Value!="qwen:Cherry" && item.Value!="@general:male" && item.Value!="@general:female" && item.Value!="@none")VoiceMappings.Remove(item.Key);
            }
            if(!VoiceMappings.ContainsKey("qwen:sex:male"))VoiceMappings["qwen:sex:male"]=QwenGeneralMaleVoice;
            if(!VoiceMappings.ContainsKey("qwen:sex:female"))VoiceMappings["qwen:sex:female"]=QwenGeneralFemaleVoice;
        }
        public static VoicePreferences Load(string path){
            if(!File.Exists(path))return new VoicePreferences();
            try{
                var prefs=JsonSerializer.Deserialize<VoicePreferences>(File.ReadAllText(path));
                if(prefs==null||!prefs.Valid())return new VoicePreferences();
                if(prefs.PlaybackVolume<0||prefs.PlaybackVolume>200)prefs.PlaybackVolume=150;
                prefs.SplitSources();return prefs;
            }catch(JsonException){return new VoicePreferences();}
        }
        public void Save(string path){
            if(!Valid())throw new ArgumentException("Voice settings require a supported provider and rate 6..20.");
            string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                File.WriteAllText(temp,JsonSerializer.Serialize(this));
                if(File.Exists(full))File.Replace(temp,full,null);else File.Move(temp,full);
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
