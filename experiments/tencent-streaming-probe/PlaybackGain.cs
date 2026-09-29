using System;
namespace QuestVoiceStreaming {
    public sealed class SpeechLeveler {
        readonly double envelopeStep,reduceStep,boostStep;
        double energy,gain=4;
        public SpeechLeveler(int sampleRate){
            if(sampleRate!=16000&&sampleRate!=24000)throw new ArgumentOutOfRangeException("sampleRate");
            envelopeStep=1-Math.Exp(-1.0/(sampleRate*0.05));
            reduceStep=1-Math.Exp(-1.0/(sampleRate*0.005));
            boostStep=1-Math.Exp(-1.0/(sampleRate*0.4));
        }
        public void Apply(byte[] data,int offset,int count,int percent){
            if(percent<0||percent>200||offset<0||count<0||(count&1)!=0||offset>data.Length-count)throw new ArgumentOutOfRangeException();
            for(int i=offset;i<offset+count;i+=2){
                double sample=(short)(data[i]|(data[i+1]<<8))/32768.0;
                energy+=envelopeStep*(sample*sample-energy);
                double rms=Math.Sqrt(Math.Max(0,energy));
                // Hold gain across silence; starting low makes every new sentence fade in.
                // Loud onsets still reduce gain quickly and pass through the peak limiter.
                double target=rms<0.008?gain:Math.Max(1,Math.Min(4,0.14/rms));
                gain+=(target<gain?reduceStep:boostStep)*(target-gain);
                double value=sample*gain*percent/100.0,magnitude=Math.Abs(value);
                if(magnitude>0.9)value=Math.Sign(value)*(0.9+0.099*Math.Tanh((magnitude-0.9)/0.099));
                int result=(int)Math.Round(value*32767);
                data[i]=(byte)(result&255);data[i+1]=(byte)((result>>8)&255);
            }
        }
    }
    public static class PlaybackGain {
        public static volatile int Percent=150;
        public static void Apply(byte[] data,int offset,int count,int percent){
            if(percent<0||percent>200||offset<0||count<0||(count&1)!=0||offset>data.Length-count)throw new ArgumentOutOfRangeException();
            if(percent==100)return;
            double gain=percent/100.0;
            for(int i=offset;i<offset+count;i+=2){
                short sample=(short)(data[i]|(data[i+1]<<8));double value=sample/32768.0*gain;
                double magnitude=Math.Abs(value);
                // Soft knee bounds amplified peaks without wrapping or hard clipping.
                if(percent>100&&magnitude>0.9)value=Math.Sign(value)*(0.9+0.099*Math.Tanh((magnitude-0.9)/0.099));
                int result=(int)Math.Round(value*32767.0);
                data[i]=(byte)(result&255);data[i+1]=(byte)((result>>8)&255);
            }
        }
    }
}
