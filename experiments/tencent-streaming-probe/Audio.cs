using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace QuestVoiceStreaming {
    public sealed class StreamingAudio : IAudioSink, IWaveProvider {
        readonly object gate=new object();
        readonly BufferedWaveProvider buffer;
        readonly SpeechLeveler leveler;
        readonly WaveOutEvent output=new WaveOutEvent();
        readonly TaskCompletionSource<bool> ended=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool completed,stopped; int underflows;
        public StreamingAudio(int sampleRate=16000) {
            if(sampleRate!=16000&&sampleRate!=24000)throw new ArgumentException("Unsupported sample rate.");
            buffer=new BufferedWaveProvider(new WaveFormat(sampleRate,16,1));
            leveler=new SpeechLeveler(sampleRate);
            buffer.BufferDuration=TimeSpan.FromSeconds(sampleRate==24000?600:60); buffer.ReadFully=true;
            output.DesiredLatency=80; output.NumberOfBuffers=2;
            output.PlaybackStopped+=delegate(object sender,StoppedEventArgs e) {
                if(e.Exception!=null) ended.TrySetException(new ProbeException("audio-device"));
                else ended.TrySetResult(true);
            };
            try { output.Init(this); } catch { output.Dispose(); throw new ProbeException("audio-device"); }
        }
        public WaveFormat WaveFormat { get { return buffer.WaveFormat; } }
        public int BufferedBytes { get { lock(gate) return buffer.BufferedBytes; } }
        public int Underruns { get { lock(gate) return underflows; } }
        public int Read(byte[] b,int offset,int count) {
            lock(gate) {
                if(stopped) return 0;
                if(!completed && buffer.BufferedBytes<count) underflows++;
                int read=buffer.Read(b,offset,count);
                leveler.Apply(b,offset,read,PlaybackGain.Percent);
                return read;
            }
        }
        public void Add(byte[] b,int count) {
            lock(gate) {
                if(stopped) throw new OperationCanceledException();
                if(ended.Task.IsCompleted) throw new ProbeException("audio-device");
                buffer.AddSamples(b,0,count);
            }
        }
        public void Start() { output.Play(); }
        public async Task Drain(CancellationToken ct) {
            lock(gate) { completed=true; buffer.ReadFully=false; }
            await ended.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        public void Stop() {
            lock(gate) { if(stopped) return; stopped=true; buffer.ClearBuffer(); }
            output.Stop();
        }
        public void Dispose() { Stop(); output.Dispose(); }
    }
}
