using System;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>Decoded PCM: interleaved float samples.</summary>
    public sealed class PcmSound
    {
        public string Path;
        public float[] Samples;
        public int Channels, Rate;
        public int Frames { get { return Samples == null || Channels <= 0 ? 0 : Samples.Length / Channels; } }
        public float Seconds { get { return Rate > 0 ? (float)Frames / Rate : 0f; } }
    }

    /// <summary>
    /// RIFF/WAVE decoding (pure). Measured census of every .wav in 3.3.5a (probe AudFormats, 20 000+ files): all are fmt tag 1
    /// PCM 16-bit, mono or stereo, 22050/44100/48000 Hz - so PCM 16 is the real path; 8-bit PCM and 32-bit float are decoded
    /// too for safety. Compressed formats (IMA ADPCM etc.) do not occur in the client and are refused with a reason.
    /// MP3 helper: AudioStart finds where MPEG data begins (229 music files carry 522 leading zero bytes).
    /// </summary>
    public static class WowWav
    {
        /// <summary>Decodes a WAV; null with a reason when it is not a RIFF/WAVE PCM file.</summary>
        public static PcmSound Decode(byte[] b, string path, out string error)
        {
            error = null;
            if (b == null) { error = "missing"; return null; }
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E')
            {
                error = "not RIFF/WAVE";
                return null;
            }
            int p = 12, tag = 0, channels = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
            while (p + 8 <= b.Length)
            {
                int len = BitConverter.ToInt32(b, p + 4);
                if (len < 0) break;
                if (b[p] == 'f' && b[p + 1] == 'm' && b[p + 2] == 't' && b[p + 3] == ' ' && len >= 16 && p + 8 + 16 <= b.Length)
                {
                    tag = BitConverter.ToUInt16(b, p + 8);
                    channels = BitConverter.ToUInt16(b, p + 10);
                    rate = BitConverter.ToInt32(b, p + 12);
                    bits = BitConverter.ToUInt16(b, p + 22);
                    if (tag == 0xFFFE && len >= 40 && p + 8 + 26 <= b.Length) tag = BitConverter.ToUInt16(b, p + 8 + 24);   // EXTENSIBLE: sub-format
                }
                else if (b[p] == 'd' && b[p + 1] == 'a' && b[p + 2] == 't' && b[p + 3] == 'a')
                {
                    dataOff = p + 8;
                    dataLen = Math.Min(len, b.Length - dataOff);
                    break;
                }
                p += 8 + len + (len & 1);
            }
            if (dataOff < 0 || dataLen <= 0) { error = "no data chunk"; return null; }
            if (channels < 1 || channels > 2 || rate < 4000 || rate > 96000) { error = "fmt " + channels + " ch " + rate + " Hz"; return null; }
            float[] s;
            if (tag == 1 && bits == 16)
            {
                int n = dataLen / 2;
                n -= n % channels;
                s = new float[n];
                for (int i = 0, o = dataOff; i < n; i++, o += 2) s[i] = (short)(b[o] | (b[o + 1] << 8)) / 32768f;
            }
            else if (tag == 1 && bits == 8)
            {
                int n = dataLen - dataLen % channels;
                s = new float[n];
                for (int i = 0; i < n; i++) s[i] = (b[dataOff + i] - 128) / 128f;
            }
            else if (tag == 3 && bits == 32)
            {
                int n = dataLen / 4;
                n -= n % channels;
                s = new float[n];
                for (int i = 0; i < n; i++) s[i] = BitConverter.ToSingle(b, dataOff + i * 4);
            }
            else
            {
                error = "fmt tag " + tag + " " + bits + " bit (only PCM is decoded)";
                return null;
            }
            if (s.Length < channels) { error = "empty"; return null; }
            return new PcmSound { Path = path, Samples = s, Channels = channels, Rate = rate };
        }

        /// <summary>Offset where MPEG audio starts: an "ID3" tag or the first frame sync (0xFF, 0xE0 mask). -1 when neither exists.</summary>
        public static int AudioStart(byte[] b)
        {
            if (b == null) return -1;
            int limit = Math.Min(b.Length - 3, 1 << 20);
            for (int i = 0; i < limit; i++)
            {
                if (b[i] == 'I' && b[i + 1] == 'D' && b[i + 2] == '3') return i;
                if (b[i] == 0xFF && (b[i + 1] & 0xE0) == 0xE0 && (b[i + 1] & 0x06) != 0) return i;   // layer bits != 00 (reserved)
                if (b[i] != 0 && i > 4096) return -1;   // not leading padding: give up
            }
            return -1;
        }

        public static bool IsMp3Path(string path)
        {
            return path != null && path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
        }
    }
}
