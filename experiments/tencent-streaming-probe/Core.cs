using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuestVoiceStreaming
{
    public sealed class ProbeException : Exception { public ProbeException(string code) : base(code) {} }
    public interface IAudioSink : IDisposable {
        int BufferedBytes { get; } int Underruns { get; }
        void Add(byte[] bytes,int count); void Start(); void Stop(); Task Drain(CancellationToken ct);
    }
    // Records only real provider PCM; playback underflow padding never enters the cache.
    public sealed class RecordingAudioSink : IAudioSink {
        readonly IAudioSink output;readonly MemoryStream pcm=new MemoryStream();
        public RecordingAudioSink(IAudioSink output){this.output=output;}
        public int BufferedBytes {get{return output.BufferedBytes;}}
        public int Underruns {get{return output.Underruns;}}
        public void Add(byte[] bytes,int count){if(pcm.Length+count>16000*2*60)throw new ProbeException("audio-limit");output.Add(bytes,count);pcm.Write(bytes,0,count);}
        public void Start(){output.Start();} public void Stop(){output.Stop();}
        public Task Drain(CancellationToken ct){return output.Drain(ct);}
        public byte[] ToArray(){return pcm.ToArray();}
        public void Dispose(){pcm.Dispose();}
    }
    public static class Signing {
        public static string Build(SortedDictionary<string,string> values,string secret) {
            string raw=String.Join("&",values.Select(p=>p.Key+"="+p.Value));
            string signature;
            using(var h=new HMACSHA1(Encoding.UTF8.GetBytes(secret)))
                signature=Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes("GETtts.cloud.tencent.com/stream_ws?"+raw)));
            return "wss://tts.cloud.tencent.com/stream_ws?"+String.Join("&",values.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value)))+"&Signature="+Uri.EscapeDataString(signature);
        }
        public static string Request(string app,string id,string key,string text,decimal rate=1.0m,string voiceType="601008") {
            int voiceNumber;if(!Int32.TryParse(voiceType,out voiceNumber)||voiceNumber<=0)throw new ProbeException("voice-type");
            if(rate<0.6m || rate>2.0m) throw new ProbeException("speed-range");
            // Tencent's Speed is piecewise, not the displayed playback multiplier.
            decimal speed=rate<=1.2m?(rate-1m)*5m:rate<=1.5m?1m+(rate-1.2m)/0.3m:2m+(rate-1.5m)*4m;
            long number;
            if(!Int64.TryParse(app,out number)||number<=0||String.IsNullOrWhiteSpace(id)||String.IsNullOrWhiteSpace(key)) throw new ProbeException("credentials");
            var v=new SortedDictionary<string,string>(StringComparer.Ordinal);
            var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            v.Add("Action","TextToStreamAudioWS"); v.Add("AppId",number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            v.Add("Codec","pcm"); v.Add("EmotionCategory","neutral"); v.Add("EmotionIntensity","100");
            v.Add("Expired",(now+300).ToString()); v.Add("SampleRate","16000"); v.Add("SecretId",id);
            v.Add("SessionId",Guid.NewGuid().ToString()); v.Add("Speed",speed.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)); v.Add("Text",text);
            v.Add("Timestamp",now.ToString()); v.Add("VoiceType",voiceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)); v.Add("Volume","0");
            return Build(v,key);
        }
    }
    public static class StreamPump {
        public const int StartupBufferMilliseconds=320;
        const int StartupBufferBytes=16000*2*StartupBufferMilliseconds/1000;
        public static async Task Run(WebSocket ws,IAudioSink audio,Action<string> mark,CancellationToken ct,int networkTimeoutMs=30000) {
            using(var network=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            network.CancelAfter(networkTimeoutMs);
            byte[] receive=new byte[8192]; int pending=-1,total=0; bool started=false,first=false;
            try {
                while(true) {
                    ct.ThrowIfCancellationRequested();
                    using(var message=new MemoryStream()) {
                        WebSocketReceiveResult r; WebSocketMessageType? kind=null;
                        do {
                            r=await ws.ReceiveAsync(new ArraySegment<byte>(receive),network.Token).ConfigureAwait(false);
                            network.Token.ThrowIfCancellationRequested();
                            if(r.MessageType==WebSocketMessageType.Close) throw new ProbeException("truncated");
                            if(kind.HasValue && kind.Value!=r.MessageType) throw new ProbeException("protocol");
                            kind=r.MessageType;
                            if(kind==WebSocketMessageType.Binary) {
                                total+=r.Count; if(total>16000*2*60) throw new ProbeException("audio-limit");
                                if(r.Count>0) {
                                    if(!first) { mark("first-audio"); first=true; }
                                    byte[] aligned=new byte[r.Count+1]; int n=0;
                                    if(pending>=0) { aligned[n++]=(byte)pending; pending=-1; }
                                    Array.Copy(receive,0,aligned,n,r.Count); n+=r.Count;
                                    if(n%2!=0) { pending=aligned[--n]; }
                                    if(n>0) audio.Add(aligned,n);
                                    if(!started && audio.BufferedBytes>=StartupBufferBytes) { audio.Start(); started=true; mark("playback-start"); }
                                }
                            } else {
                                if(message.Length+r.Count>131072) throw new ProbeException("message-limit");
                                message.Write(receive,0,r.Count);
                            }
                        } while(!r.EndOfMessage);
                        if(kind==WebSocketMessageType.Text) {
                            bool final=false;
                            try {
                                using(var doc=JsonDocument.Parse(message.ToArray())) {
                                    JsonElement code, end;
                                    if(!doc.RootElement.TryGetProperty("code",out code)) throw new ProbeException("protocol");
                                    int value=code.GetInt32(); if(value!=0) throw new ProbeException("provider-"+value);
                                    final=doc.RootElement.TryGetProperty("final",out end) && end.GetInt32()==1;
                                }
                            } catch(JsonException) { throw new ProbeException("protocol"); }
                              catch(InvalidOperationException) { throw new ProbeException("protocol"); }
                              catch(FormatException) { throw new ProbeException("protocol"); }
                            if(final) {
                                if(total==0 || pending>=0) throw new ProbeException("empty-or-odd");
                                mark("stream-finished");
                                if(!started) { audio.Start(); mark("playback-start"); }
                                // Close only this finished connection; queued PCM continues to drain.
                                ws.Abort();
                                network.CancelAfter(Timeout.Infinite);
                                await audio.Drain(ct).ConfigureAwait(false); return;
                            }
                        }
                    }
                }
            } catch { audio.Stop(); throw; }
            }
        }
    }
}
