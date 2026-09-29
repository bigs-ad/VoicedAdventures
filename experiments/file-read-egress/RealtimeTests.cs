using System;
using System.Text;
class RealtimeTests
{
    static void Assert(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
    static string Bits(byte[] bytes) {
        var s = new StringBuilder();
        foreach (byte b in bytes) s.Append(Convert.ToString(b, 2).PadLeft(8, '0'));
        return s.ToString();
    }
    static string Feed(string bits) {
        var d = new FrameDecoder(); string text = null;
        foreach (char bit in bits) text = d.Add(bit);
        return text;
    }
    static int Main() {
        try {
            string expected = "\u4f60\u597d";
            byte[] frame = FrameDecoder.Encode(expected);
            Assert(frame.Length == 11);
            string bits = Bits(frame);
            Assert(Feed(bits) == expected);
            Assert(Feed(bits.Substring(0, 87)) == null);
            for (int i=0; i<bits.Length; i++) {
                string bad = bits.Substring(0,i)+(bits[i]=='0'?'1':'0')+bits.Substring(i+1);
                bool rejected = false;
                try { rejected = Feed(bad) == null; } catch (FormatException) { rejected = true; }
                Assert(rejected);
            }
            bool extra = false;
            try { Feed(bits+"0"); } catch (FormatException) { extra=true; }
            Assert(extra);
            Assert(Feed(Bits(FrameDecoder.Encode("different text"))) == "different text");
            var hexDecoder=new FrameDecoder();string hexText=null;
            foreach(byte value in frame) {
                Assert(hexDecoder.AddNibble(value >> 4)==null);
                hexText=hexDecoder.AddNibble(value & 15);
            }
            Assert(hexText==expected && hexDecoder.BitCount==88);
            for(int n=0;n<16;n++) {
                var d=new FrameDecoder();
                d.AddNibble(5);d.AddNibble(1);d.AddNibble(5);d.AddNibble(6);
                Assert(d.AddNibble(n)==null);
            }
            bool invalidNibble=false;
            try { new FrameDecoder().AddNibble(16); } catch(ArgumentOutOfRangeException) { invalidNibble=true; }
            Assert(invalidNibble);
            var sequence=new FrameSequenceDecoder(3);
            for(int n=1;n<=3;n++) {
                string text=n+":long repeat \u4f60\u597d";
                string actual=null;
                foreach(byte value in FrameDecoder.Encode(text)) {
                    Assert(sequence.Add(value>>4,4)==null);
                    actual=sequence.Add(value&15,4);
                }
                Assert(actual==text && sequence.Completed==n);
            }
            bool rejectedExtra=false;
            try { sequence.Add(0,4); } catch(FormatException) { rejectedExtra=true; }
            Assert(rejectedExtra);
            var broken=new FrameSequenceDecoder(3);
            bool rejectedBroken=false;
            try { broken.Add(0,4);broken.Add(0,4); } catch(FormatException) { rejectedBroken=true; }
            Assert(rejectedBroken && broken.Completed==0);
            Assert(FrameDecoder.DecodePayload(new byte[]{255,96,79,125,89})==expected);
            Assert(FrameDecoder.DecodePayload(Encoding.UTF8.GetBytes("ASCII"))=="ASCII");
            bool badUtf16=false;
            try { FrameDecoder.DecodePayload(new byte[]{255,0,216}); } catch(FormatException) { badUtf16=true; }
            Assert(badUtf16);
            bool oddUtf16=false;
            try { FrameDecoder.DecodePayload(new byte[]{255,1}); } catch(FormatException) { oddUtf16=true; }
            Assert(oddUtf16);
            Console.WriteLine("PASS streaming decoder, variable payload, corruption, truncation and extra symbols");
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
