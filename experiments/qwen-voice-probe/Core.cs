using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VoicedAdventures.Qwen {
    public sealed class QwenFailure : Exception {public QwenFailure(string message):base(message){}}
    public sealed class StreamEvent {public string Type;public byte[] Audio;}
    public static class Protocol {
        public const string Model="qwen3-tts-flash-realtime-2025-11-27";
        public const string StreamUrl="wss://dashscope.aliyuncs.com/api-ws/v1/realtime?model="+Model;
        public const int MaxBytes=12*1024*1024;
        public static StreamEvent ReadEvent(string json){
            using(var doc=JsonDocument.Parse(json)){
                var root=doc.RootElement;var result=new StreamEvent {Type=root.GetProperty("type").GetString()};
                if(result.Type=="error")throw new QwenFailure("阿里云返回合成错误，请检查 Key 权限、额度和音色所属账号。未自动重试。");
                if(result.Type=="response.audio.delta"){
                    result.Audio=Convert.FromBase64String(root.GetProperty("delta").GetString());
                    if(result.Audio.Length%2!=0)throw new QwenFailure("收到不完整 PCM 音频帧。");
                }
                if(result.Type=="response.done" && root.GetProperty("response").GetProperty("status").GetString()!="completed")throw new QwenFailure("云端合成未完整结束，本次音频不缓存。");
                return result;
            }
        }
        static string Id(){return "event_"+Guid.NewGuid().ToString("N");}
        public static string Session(string voice,decimal rate=1m){
            if(rate<0.5m||rate>2m)throw new QwenFailure("Invalid speech rate.");
            return JsonSerializer.Serialize(new {event_id=Id(),type="session.update",session=new {voice=voice,mode="commit",language_type="Chinese",response_format="pcm",sample_rate=24000,speech_rate=rate}});
        }
        public static string Append(string text){return JsonSerializer.Serialize(new {event_id=Id(),type="input_text_buffer.append",text=text});}
        public static string Event(string type){return JsonSerializer.Serialize(new {event_id=Id(),type=type});}
    }
    public interface IQwenChannel : IDisposable {
        Task Connect(string key,CancellationToken ct);
        Task Send(string text,CancellationToken ct);
        Task<string> Receive(CancellationToken ct);
    }
    public sealed class QwenChannel : IQwenChannel {
        public Uri Endpoint {get;private set;}
        public QwenChannel(string model=Protocol.Model){
            if(model!=Protocol.Model)throw new QwenFailure("Unsupported realtime model.");
            Endpoint=new Uri("wss://dashscope.aliyuncs.com/api-ws/v1/realtime?model="+model);
        }
        readonly ClientWebSocket socket=new ClientWebSocket();
        public async Task Connect(string key,CancellationToken ct){socket.Options.SetRequestHeader("Authorization","Bearer "+key);await socket.ConnectAsync(Endpoint,ct).ConfigureAwait(false);}
        public Task Send(string text,CancellationToken ct){byte[] b=Encoding.UTF8.GetBytes(text);return socket.SendAsync(new ArraySegment<byte>(b),WebSocketMessageType.Text,true,ct);}
        public async Task<string> Receive(CancellationToken ct){
            using(var output=new MemoryStream()){
                byte[] buffer=new byte[16384];WebSocketReceiveResult result;
                do{
                    result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),ct).ConfigureAwait(false);
                    if(result.MessageType==WebSocketMessageType.Close)return null;
                    if(result.MessageType!=WebSocketMessageType.Text||output.Length+result.Count>Protocol.MaxBytes)throw new QwenFailure("实时接口返回格式异常。");
                    output.Write(buffer,0,result.Count);
                }while(!result.EndOfMessage);
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
        public void Dispose(){socket.Abort();socket.Dispose();}
    }
    public sealed class QwenClient {
        static async Task WaitFor(IQwenChannel socket,string type,CancellationToken ct){
            for(int n=0;n<30;n++){
                string json=await socket.Receive(ct).ConfigureAwait(false);if(json==null)break;
                if(Protocol.ReadEvent(json).Type==type)return;
            }
            throw new QwenFailure("实时连接未完成握手。");
        }
        public async Task<byte[]> Synthesize(IQwenChannel channel,string key,string voice,string text,Action<byte[]> audio,Action<string> log,CancellationToken ct,decimal rate=1m){
            if(String.IsNullOrWhiteSpace(voice)||String.IsNullOrWhiteSpace(text)||text.Length>600)throw new QwenFailure("请选择音色，并填写 1–600 字台词。");
            var clock=Stopwatch.StartNew();
            using(channel)using(var data=new MemoryStream()){
                await channel.Connect(key,ct).ConfigureAwait(false);log("连接已建立："+clock.ElapsedMilliseconds+" ms");
                await WaitFor(channel,"session.created",ct).ConfigureAwait(false);
                await channel.Send(Protocol.Session(voice,rate),ct).ConfigureAwait(false);
                await WaitFor(channel,"session.updated",ct).ConfigureAwait(false);
                await channel.Send(Protocol.Append(text),ct).ConfigureAwait(false);
                await channel.Send(Protocol.Event("input_text_buffer.commit"),ct).ConfigureAwait(false);
                await channel.Send(Protocol.Event("session.finish"),ct).ConfigureAwait(false);
                bool completed=false;
                while(true){
                    string json=await channel.Receive(ct).ConfigureAwait(false);
                    if(json==null)throw new QwenFailure("连接提前结束，本次音频不缓存。");
                    var e=Protocol.ReadEvent(json);
                    if(e.Audio!=null && e.Audio.Length>0){
                        if(data.Length==0)log("收到首段音频："+clock.ElapsedMilliseconds+" ms");
                        if(data.Length+e.Audio.Length>32*1024*1024)throw new QwenFailure("音频超出长度限制。");
                        data.Write(e.Audio,0,e.Audio.Length);audio(e.Audio);
                    }
                    if(e.Type=="response.done")completed=true;
                    if(e.Type=="session.finished"){
                        if(!completed||data.Length==0)throw new QwenFailure("未收到完整语音，本次不缓存。");
                        log("整段合成完成："+clock.ElapsedMilliseconds+" ms");return data.ToArray();
                    }
                }
            }
        }
    }
}
