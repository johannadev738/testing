using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;

// Local Windows/.NET Framework helper. Never opens a microphone or network connection.
// Input/output stay in memory. Do not log transcript or synthesized text.
internal static class WindowsVoiceHost
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) return Fail("BAD_REQUEST");
            if (args[0] == "probe")
            {
                using (var synth = new SpeechSynthesizer())
                    Console.WriteLine("recognizers=" + SpeechRecognitionEngine.InstalledRecognizers().Count +
                        ";voices=" + synth.GetInstalledVoices().Count(v => v.Enabled));
                return 0;
            }
            if (args[0] == "synthesize") return Synthesize();
            if (args[0] == "recognize") return Recognize();
            return Fail("BAD_REQUEST");
        }
        catch (Exception) { return Fail("SPEECH_ENGINE_ERROR"); }
    }

    static int Synthesize()
    {
        int rate;
        if (!int.TryParse(ReadLine(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out rate) || rate < -3 || rate > 3)
            return Fail("BAD_REQUEST");
        string voice = DecodeText(ReadLine(1024));
        string text = DecodeText(ReadLine(4096));
        if (voice.Length > 128 || string.IsNullOrWhiteSpace(text) || text.Length > 512) return Fail("BAD_REQUEST");
        using (var synth = new SpeechSynthesizer())
        using (var output = new MemoryStream())
        {
            var installed = synth.GetInstalledVoices().Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en").ToArray();
            if (installed.Length == 0) return Fail("NO_ENGLISH_VOICE");
            if (voice.Length == 0) voice = installed[0].VoiceInfo.Name;
            if (!installed.Any(v => v.VoiceInfo.Name == voice)) return Fail("VOICE_UNAVAILABLE");
            synth.SelectVoice(voice);
            synth.Rate = rate;
            synth.Volume = 100;
            synth.SetOutputToAudioStream(output, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synth.Speak(text);
            synth.SetOutputToNull();
            if (output.Length > 960000) return Fail("AUDIO_TOO_LONG");
            Console.WriteLine(Convert.ToBase64String(WrapWave(output.ToArray())));
            return 0;
        }
    }

    static int Recognize()
    {
        byte[] wave = Convert.FromBase64String(ReadLine(8000000));
        if (wave.Length < 44 || wave.Length > 6000000) return Fail("BAD_AUDIO");
        var info = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en");
        if (info == null) return Fail("NO_ENGLISH_RECOGNIZER");
        using (var input = new MemoryStream(wave, false))
        using (var completed = new ManualResetEvent(false))
        using (var recognizer = new SpeechRecognitionEngine(info))
        {
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SetInputToWaveStream(input);
            var transcript = new StringBuilder();
            bool tooLong = false, engineError = false;
            recognizer.SpeechRecognized += delegate(object sender, SpeechRecognizedEventArgs e)
            {
                if (tooLong || string.IsNullOrWhiteSpace(e.Result.Text)) return;
                if (transcript.Length > 0) transcript.Append(' ');
                transcript.Append(e.Result.Text);
                if (transcript.Length > 512) { tooLong = true; recognizer.RecognizeAsyncCancel(); }
            };
            recognizer.RecognizeCompleted += delegate(object sender, RecognizeCompletedEventArgs e)
            { engineError = e.Error != null; completed.Set(); };
            recognizer.RecognizeAsync(RecognizeMode.Multiple);
            if (!completed.WaitOne(15000)) { recognizer.RecognizeAsyncCancel(); return Fail("RECOGNITION_TIMEOUT"); }
            if (engineError) return Fail("SPEECH_ENGINE_ERROR");
            if (tooLong) return Fail("TRANSCRIPT_TOO_LONG");
            if (transcript.Length == 0) return Fail("NO_RECOGNITION");
            Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(transcript.ToString())));
            return 0;
        }
    }

    static string ReadLine(int maximum)
    {
        string value = Console.ReadLine();
        if (value == null || value.Length > maximum) throw new InvalidDataException();
        return value;
    }
    static byte[] WrapWave(byte[] pcm)
    {
        using (var wave = new MemoryStream())
        using (var writer = new BinaryWriter(wave))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
            writer.Flush(); return wave.ToArray();
        }
    }
    static string DecodeText(string value) { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value)); }
    static int Fail(string code) { Console.Error.WriteLine(code); return 1; }
}
