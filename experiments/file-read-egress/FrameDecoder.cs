using System;
using System.Collections.Generic;
using System.Text;

public sealed class FrameDecoder
{
    readonly List<byte> bytes = new List<byte>();
    int partial, bits;
    bool finished;
    public int BitCount { get { return bytes.Count * 8 + bits; } }
    public string AddNibble(int value)
    {
        if(value<0 || value>15) throw new ArgumentOutOfRangeException("value");
        if(bits%4!=0) throw new FormatException("Unaligned nibble");
        string text=null;
        for(int i=3;i>=0;i--) text=Add(((value>>i)&1)==0?'0':'1');
        return text;
    }
    public string Add(char bit)
    {
        if (finished || (bit != '0' && bit != '1')) throw new FormatException("Unexpected symbol");
        partial = partial * 2 + (bit - '0'); bits++;
        if (bits != 8) return null;
        bytes.Add((byte)partial); bits=0; partial=0;
        if ((bytes.Count == 1 && bytes[0] != 81) || (bytes.Count == 2 && bytes[1] != 86))
            throw new FormatException("Bad frame header");
        if (bytes.Count < 3 || bytes.Count < bytes[2] + 5) return null;
        int a=0,b=0;
        for(int i=0;i<bytes.Count-2;i++) { a=(a+bytes[i])%255; b=(b+a)%255; }
        if(bytes[bytes.Count-2]!=a || bytes[bytes.Count-1]!=b) throw new FormatException("Bad checksum");
        finished=true;
        return DecodePayload(bytes.GetRange(3,bytes[2]).ToArray());
    }
    public static string DecodePayload(byte[] payload)
    {
        try {
            if(payload.Length>0 && payload[0]==255) {
                if((payload.Length-1)%2!=0) throw new FormatException("Invalid UTF-16 length");
                return new UnicodeEncoding(false,false,true).GetString(payload,1,payload.Length-1);
            }
            return new UTF8Encoding(false,true).GetString(payload);
        } catch(DecoderFallbackException) { throw new FormatException("Invalid text encoding"); }
    }
    public static byte[] Encode(string text)
    {
        byte[] payload = new UTF8Encoding(false,true).GetBytes(text);
        if(payload.Length>255) throw new ArgumentException("Payload too long");
        byte[] frame=new byte[payload.Length+5];
        frame[0]=81; frame[1]=86; frame[2]=(byte)payload.Length;
        Array.Copy(payload,0,frame,3,payload.Length);
        int a=0,b=0;
        for(int i=0;i<frame.Length-2;i++) { a=(a+frame[i])%255;b=(b+a)%255; }
        frame[frame.Length-2]=(byte)a;frame[frame.Length-1]=(byte)b;
        return frame;
    }
}

public sealed class FrameSequenceDecoder
{
    readonly int expected;
    FrameDecoder current = new FrameDecoder();
    bool failed;
    public int Completed { get; private set; }
    public int BitCount { get; private set; }
    public FrameSequenceDecoder(int expectedFrames) {
        if(expectedFrames<1 || expectedFrames>3) throw new ArgumentOutOfRangeException("expectedFrames");
        expected=expectedFrames;
    }
    public string Add(int value,int width) {
        if(failed || Completed>=expected) throw new FormatException("Unexpected extra or invalid frame");
        if((width!=1 && width!=4) || value<0 || value>=(1<<width)) throw new ArgumentOutOfRangeException("value");
        try {
            string text=width==4?current.AddNibble(value):current.Add(value==0?'0':'1');
            BitCount+=width;
            if(text!=null) { Completed++;current=new FrameDecoder(); }
            return text;
        } catch(FormatException) { failed=true;throw; }
    }
}
