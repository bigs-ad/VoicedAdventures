using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoicedAdventures.Qwen;

namespace QuestVoiceStreaming {
    public sealed partial class ProbeWindow {
        async Task<byte[]> PlayQwen(string voice,string text,decimal speed,string cacheKey,CancellationToken ct,Stopwatch watch,Action<string> mark,string model=Protocol.Model){
            if(diagnosticQwen!=null){cloudCalls++;return await diagnosticQwen(text,ct);}
            string savedKey;
            try{savedKey=qwenStore.LoadKey();}catch{throw new QwenFailure("无法读取本机千问凭据，请在音源设置重新保存北京 Key。");}
            if(String.IsNullOrWhiteSpace(savedKey))throw new QwenFailure("尚未保存北京 Key，请先在音源设置中填写。");
            // Open the audio device before sending a billable request.
            audio=new StreamingAudio(24000);var output=audio;bool started=false,first=true;
            ct.ThrowIfCancellationRequested();cloudCalls++;watch.Start();
            Record("千问 · "+model+" · 本次语速："+speed.ToString("0.0#")+" 倍 · 未命中缓存");
            Record("开播预缓冲：320 ms 音频（非固定等待时长）");
            Action<string> notify=eventName=>{if(!closing&&!IsDisposed)BeginInvoke(new Action(()=>mark(eventName)));};
            byte[] pcm;
            using(var network=CancellationTokenSource.CreateLinkedTokenSource(ct)){
            network.CancelAfter(TimeSpan.FromSeconds(90));
            pcm=await new QwenClient().Synthesize(new QwenChannel(model),savedKey,voice,text,chunk=>{
                ct.ThrowIfCancellationRequested();if(first){first=false;notify("first-audio");}
                var playable=chunk;
                output.Add(playable,playable.Length);
                if(!started&&output.BufferedBytes>=15360){started=true;output.Start();notify("playback-start");}
            },Record,network.Token,speed);
            }
            ct.ThrowIfCancellationRequested();
            if(!started){output.Start();mark("playback-start");}
            await output.Drain(ct);ct.ThrowIfCancellationRequested();
            RememberAudio(cacheKey,pcm);
            Record("缓冲不足次数："+output.Underruns);
            return pcm;
        }
    }
}
