using System;
using QuestVoiceStreaming;
class PlaybackGainTests {
    static void Check(bool ok){if(!ok)throw new Exception("gain regression");}
    static int Main(){
        byte[] original={0x10,0x27,0xf0,0xd8,0xff,0x7f,0,0x80};
        var b=(byte[])original.Clone();PlaybackGain.Apply(b,0,b.Length,100);Check(BitConverter.ToInt16(b,0)==10000&&b[4]==255);
        b=(byte[])original.Clone();PlaybackGain.Apply(b,0,b.Length,150);Check(BitConverter.ToInt16(b,0)==15000&&BitConverter.ToInt16(b,2)==-15000);
        Check(BitConverter.ToInt16(b,4)>30000&&BitConverter.ToInt16(b,4)<32767&&BitConverter.ToInt16(b,6)<-30000);
        PlaybackGain.Apply(b,0,b.Length,0);foreach(byte x in b)Check(x==0);
        b=(byte[])original.Clone();PlaybackGain.Apply(b,2,2,200);Check(b[0]==original[0]&&b[4]==original[4]);
        foreach(int rate in new[]{16000,24000}) {
            foreach(int amplitude in new[]{800,1600,3200}) {
                var phrases=new byte[rate*6];
                for(int phrase=0;phrase<3;phrase++)for(int i=rate/3;i<rate;i++){
                    short s=(short)(amplitude*Math.Sin(i*2*Math.PI*220/rate));int at=(phrase*rate+i)*2;
                    phrases[at]=(byte)s;phrases[at+1]=(byte)(s>>8);
                }
                new SpeechLeveler(rate).Apply(phrases,0,phrases.Length,150);
                for(int phrase=0;phrase<3;phrase++){
                    double first=0,last=0;
                    for(int i=0;i<rate/12;i++){
                        double a=BitConverter.ToInt16(phrases,(phrase*rate+rate/3+i)*2);
                        double b2=BitConverter.ToInt16(phrases,(phrase*rate+rate*5/6+i)*2);
                        first+=a*a;last+=b2*b2;
                    }
                    if(first<last*0.95)throw new Exception("Fade after an internal sentence pause");
                }
            }
            var opening=new byte[rate*4];
            for(int i=rate/2;i<rate*2;i++){short s=(short)(1600*Math.Sin(i*2*Math.PI*220/rate));opening[i*2]=(byte)s;opening[i*2+1]=(byte)(s>>8);}
            new SpeechLeveler(rate).Apply(opening,0,opening.Length,150);
            double startPower=0,endPower=0;
            for(int i=0;i<rate/10;i++){
                double start=BitConverter.ToInt16(opening,(rate/2+i)*2),end=BitConverter.ToInt16(opening,(rate*3/2+i)*2);
                startPower+=start*start;endPower+=end*end;
            }
            if(startPower<endPower*0.95)throw new Exception("Quiet sentence fades in after leading silence");
            var quiet=new byte[rate*2];double before=0,after=0;
            for(int i=0;i<rate;i++){short s=(short)(1600*Math.Sin(i*2*Math.PI*220/rate));quiet[i*2]=(byte)s;quiet[i*2+1]=(byte)(s>>8);before+=(double)s*s;}
            var split=(byte[])quiet.Clone();var leveler=new SpeechLeveler(rate);leveler.Apply(quiet,0,quiet.Length,150);
            var other=new SpeechLeveler(rate);for(int offset=0;offset<split.Length;offset+=320)other.Apply(split,offset,Math.Min(320,split.Length-offset),150);
            for(int i=0;i<quiet.Length;i+=2){int s=BitConverter.ToInt16(quiet,i);after+=(double)s*s;Check(Math.Abs(s)<=32735);Check(quiet[i]==split[i]&&quiet[i+1]==split[i+1]);}
            Check(after>before*9);
            var silence=new byte[rate*2];leveler.Apply(silence,0,silence.Length,150);foreach(byte x in silence)Check(x==0);
            leveler.Apply(quiet,0,quiet.Length,0);foreach(byte x in quiet)Check(x==0);
        }
        Console.WriteLine("PASS gain and speech leveling: quiet voice lift, bounds, chunk invariance, silence and mute at 16/24 kHz");return 0;
    }
}
