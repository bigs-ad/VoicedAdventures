using System;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using System.Text;

namespace QuestVoiceStreaming {
    public static class LocalSpeechHost {
        public static int Main(string[] args){
            Console.OutputEncoding=new UTF8Encoding(false);
            try {
                bool selectedOnly=args.Length==1&&args[0]=="--selected";
                bool inventory=args.Length==1&&args[0]=="--voices";
                bool check=args.Length==1&&(args[0]=="--check"||selectedOnly||inventory);
                bool pcm=(args.Length==2||args.Length==3)&&args[1]=="--pcm";
                decimal rate=1m;
                if(!check&&((args.Length!=1&&!pcm)||!Decimal.TryParse(args[0],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out rate)||rate<0.6m||rate>2m))
                    throw new ArgumentException("Invalid local speech rate (expected 0.6 to 2.0).");
                string text=null;
                if(!check){
                    byte[] buffer=new byte[4097];int count=0;
                    using(Stream input=Console.OpenStandardInput()){
                        while(count<buffer.Length){int read=input.Read(buffer,count,buffer.Length-count);if(read==0)break;count+=read;}
                    }
                    if(count>4096)throw new ArgumentException("Speech text exceeds 4096 UTF-8 bytes.");
                    text=new UTF8Encoding(false,true).GetString(buffer,0,count);
                    if(String.IsNullOrWhiteSpace(text))throw new ArgumentException("Speech text is required.");
                }
                using(var synth=new SpeechSynthesizer()){
                    string selected=null;
                    bool exactVoice=false;
                    foreach(InstalledVoice voice in synth.GetInstalledVoices()){
                        if(!voice.Enabled||voice.VoiceInfo.Culture.TwoLetterISOLanguageName!="zh")continue;
                        if(inventory)Console.WriteLine(voice.VoiceInfo.Name+"\t"+voice.VoiceInfo.Gender.ToString().ToLowerInvariant()+"\t"+voice.VoiceInfo.Culture.Name);
                        else if(check&&!selectedOnly)Console.WriteLine(voice.VoiceInfo.Name+" ("+voice.VoiceInfo.Culture.Name+")");
                        if(selected==null||voice.VoiceInfo.Culture.Name=="zh-CN")selected=voice.VoiceInfo.Name;
                        if(pcm&&args.Length==3&&voice.VoiceInfo.Name==args[2])exactVoice=true;
                    }
                    if(inventory)return 0;
                    if(selected==null){Console.Error.WriteLine("no-chinese-voice: Install an enabled Chinese Windows speech voice; English fallback is disabled.");return 2;}
                    if(pcm&&args.Length==3){if(!exactVoice)throw new InvalidOperationException("Selected local voice unavailable; synthesis refused.");selected=args[2];}
                    if(check){if(selectedOnly)Console.WriteLine(selected);return 0;}
                    synth.SelectVoice(selected);
                    synth.Rate=Math.Max(-10,Math.Min(10,(int)Math.Round(10*Math.Log((double)rate,2))));
                    if(pcm){
                        // System.Speech seeks its output; a redirected stdout pipe cannot seek.
                        using(var buffer=new MemoryStream()){
                            synth.SetOutputToAudioStream(buffer,new SpeechAudioFormatInfo(16000,AudioBitsPerSample.Sixteen,AudioChannel.Mono));
                            synth.Speak(text);synth.SetOutputToNull();
                            if(buffer.Length==0||buffer.Length>4*1024*1024)throw new InvalidOperationException("Invalid synthesized PCM length.");
                            byte[] data=buffer.ToArray();
                            using(Stream output=Console.OpenStandardOutput()){output.Write(data,0,data.Length);output.Flush();}
                        }
                    }else{synth.SetOutputToDefaultAudioDevice();synth.Speak(text);}
                }
                return 0;
            }catch(Exception ex){Console.Error.WriteLine("local-speech-error: "+ex.Message);return 1;}
        }
    }
}
