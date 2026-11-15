using System;
using System.IO;

namespace SimSafe.Core
{
    /// <summary>Bounded RIFF/WAVE PCM16 transport for local speech providers; no file persistence.</summary>
    public static class PcmWaveCodec
    {
        const int MaxDataBytes = 192000 * 2 * 30 * 2;

        public static byte[] Encode(PcmAudio audio)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            var samples = audio.CopySamples();
            using (var stream = new MemoryStream(44 + samples.Length * 2))
            using (var writer = new BinaryWriter(stream))
            {
                WriteTag(writer, "RIFF"); writer.Write(36 + samples.Length * 2); WriteTag(writer, "WAVE");
                WriteTag(writer, "fmt "); writer.Write(16); writer.Write((short)1); writer.Write((short)audio.Channels);
                writer.Write(audio.SampleRate); writer.Write(audio.SampleRate * audio.Channels * 2);
                writer.Write((short)(audio.Channels * 2)); writer.Write((short)16);
                WriteTag(writer, "data"); writer.Write(samples.Length * 2);
                foreach (float sample in samples)
                    writer.Write((short)Math.Round(sample * (sample < 0 ? 32768d : 32767d)));
                writer.Flush(); return stream.ToArray();
            }
        }

        public static PcmAudio Decode(byte[] wave)
        {
            if (wave == null) throw new ArgumentNullException(nameof(wave));
            if (wave.Length < 44 || wave.Length > MaxDataBytes + 4096 ||
                !Tag(wave, 0, "RIFF") || !Tag(wave, 8, "WAVE") || UInt(wave, 4) != wave.Length - 8)
                throw Invalid();
            int channels = 0, rate = 0, dataOffset = 0, dataLength = 0;
            bool formatFound = false, dataFound = false;
            long cursor = 12;
            while (cursor < wave.Length)
            {
                if (cursor + 8 > wave.Length) throw Invalid();
                int at = (int)cursor;
                uint size = UInt(wave, at + 4);
                long end = cursor + 8 + size;
                if (end > wave.Length) throw Invalid();
                if (Tag(wave, at, "fmt "))
                {
                    if (formatFound || size < 16 || UShort(wave, at + 8) != 1 || UShort(wave, at + 22) != 16) throw Invalid();
                    channels = UShort(wave, at + 10);
                    uint sampleRate = UInt(wave, at + 12);
                    if (channels < 1 || channels > 2 || sampleRate < 8000 || sampleRate > 192000) throw Invalid();
                    rate = (int)sampleRate;
                    if (UShort(wave, at + 20) != channels * 2 || UInt(wave, at + 16) != rate * channels * 2) throw Invalid();
                    formatFound = true;
                }
                else if (Tag(wave, at, "data"))
                {
                    if (dataFound || size == 0 || size > MaxDataBytes) throw Invalid();
                    dataOffset = at + 8; dataLength = (int)size; dataFound = true;
                }
                cursor = end + (size & 1);
                if (cursor > wave.Length) throw Invalid();
            }
            if (!formatFound || !dataFound || dataLength % (channels * 2) != 0 ||
                dataLength > (long)rate * channels * 30 * 2) throw Invalid();
            var samples = new float[dataLength / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short sample = unchecked((short)UShort(wave, dataOffset + i * 2));
                samples[i] = sample / (sample < 0 ? 32768f : 32767f);
            }
            return new PcmAudio(samples, rate, channels);
        }

        static void WriteTag(BinaryWriter writer, string value)
        {
            foreach (char c in value) writer.Write((byte)c);
        }
        static bool Tag(byte[] bytes, int offset, string tag)
        {
            for (int i = 0; i < 4; i++) if (bytes[offset + i] != tag[i]) return false;
            return true;
        }
        static int UShort(byte[] bytes, int offset) => bytes[offset] | (bytes[offset + 1] << 8);
        static uint UInt(byte[] bytes, int offset) => (uint)(bytes[offset] | (bytes[offset + 1] << 8) |
            (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
        static InvalidDataException Invalid() => new InvalidDataException("Expected a bounded, complete PCM16 RIFF/WAVE clip.");
    }
}
