using System;
using System.IO;
namespace QuestVoiceStreaming {
    public static class ReleaseProviderTests {
        public static void Main() {
            string path=Path.Combine(Path.GetTempPath(),"va-providers-"+Guid.NewGuid().ToString("N")+".json");
            try {
                foreach(string provider in new[]{"system","tencent","qwen"}) {
                    new VoicePreferences {Provider=provider}.Save(path);
                    if(VoicePreferences.Load(path).Provider!=provider)throw new Exception("Provider round trip: "+provider);
                }
                foreach(string provider in new[]{"localpack","hybrid","invalid"}) {
                    bool rejected=false;try{new VoicePreferences {Provider=provider}.Save(path);}catch(ArgumentException){rejected=true;}
                    if(!rejected)throw new Exception("Unsupported provider was saved: "+provider);
                    File.WriteAllText(path,"{\"Provider\":\""+provider+"\",\"Rate\":10}");
                    if(VoicePreferences.Load(path).Provider!="system")throw new Exception("Unsafe persisted provider");
                    rejected=false;try{SpeakerVoiceRouting.Resolve(provider,new GameMessage());}catch(ArgumentException){rejected=true;}
                    if(!rejected)throw new Exception("Unsupported provider was routed");
                }
                File.WriteAllText(path,"{\"Provider\":\"qwen\",\"Rate\":10,\"SourceSchema\":1,\"VoiceMappings\":{\"qwen:sex:male\":\"qwen-vc:obsolete\",\"tencent:sex:female\":\"501002\"}}");
                var imported=VoicePreferences.Load(path);
                if(SpeakerVoiceRouting.Resolve("qwen",new GameMessage {SpeakerSex="male"},imported.VoiceMappings).CloudVoice!="Ethan" || imported.VoiceMappings["tencent:sex:female"]!="501002")throw new Exception("Obsolete Qwen mappings were retained");
                File.WriteAllText(path,"{\"Provider\":\"tencent\",\"Rate\":10,\"SourceSchema\":0,\"VoiceMappings\":{\"tencent:sex:male\":\"qwen-vd:obsolete\",\"tencent:sex:female\":\"501002\"}}");
                imported=VoicePreferences.Load(path);
                if(SpeakerVoiceRouting.Resolve("tencent",new GameMessage {SpeakerSex="male"},imported.VoiceMappings).Identity!="601008" || imported.VoiceMappings["tencent:sex:female"]!="501002")throw new Exception("Cross-provider legacy voice retained");
                var male=SpeakerVoiceRouting.Resolve("qwen",new GameMessage {SpeakerSex="male"});
                var female=SpeakerVoiceRouting.Resolve("qwen",new GameMessage {SpeakerSex="female"});
                if(!male.IsQwen||male.CloudVoice!="Ethan"||female.CloudVoice!="Cherry"||male.QwenModel!="qwen3-tts-flash-realtime-2025-11-27")throw new Exception("Public Qwen defaults");
                Console.WriteLine("Release provider tests passed.");
            } finally {if(File.Exists(path))File.Delete(path);}
        }
    }
}
