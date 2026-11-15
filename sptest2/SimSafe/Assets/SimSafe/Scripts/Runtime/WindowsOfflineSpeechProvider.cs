using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimSafe.Core;

namespace SimSafe
{
    /// <summary>
    /// Local Windows-only speech adapter. Audio/text use redirected in-memory pipes, not files,
    /// shell interpolation, command-line transcripts, logs or external services.
    /// </summary>
    public sealed class WindowsOfflineSpeechProvider : ISpeechToTextProvider, ITextToSpeechProvider
    {
        readonly string hostPath, voiceName;
        readonly int rate;
        public bool IsAvailable
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return File.Exists(hostPath);
#else
                return false;
#endif
            }
        }

        public WindowsOfflineSpeechProvider(string hostPath, int rate = 0, string voiceName = "")
        {
            if (string.IsNullOrWhiteSpace(hostPath)) throw new ArgumentException("A local speech-host path is required.", nameof(hostPath));
            if (rate < -3 || rate > 3) throw new ArgumentOutOfRangeException(nameof(rate));
            if (voiceName == null || voiceName.Length > 128) throw new ArgumentException("Voice name must be bounded.", nameof(voiceName));
            this.hostPath = Path.GetFullPath(hostPath); this.rate = rate; this.voiceName = voiceName;
        }

        public async Task<string> TranscribeAsync(PcmAudio audio, CancellationToken cancellationToken)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            if (audio.SampleRate > 48000 || audio.SampleCount > (long)audio.SampleRate * audio.Channels * 15)
                throw new ArgumentException("Record at most 15 seconds at 48 kHz or below.", nameof(audio));
            string output = await RunAsync("recognize", Convert.ToBase64String(PcmWaveCodec.Encode(audio)), cancellationToken);
            string text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(output));
            if (string.IsNullOrWhiteSpace(text) || text.Length > 512) throw new InvalidOperationException("No bounded transcript was returned. Try again or type your message.");
            return text;
        }

        public async Task<PcmAudio> SynthesizeAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 512) throw new ArgumentException("Speech text must contain 1 to 512 characters.", nameof(text));
            string input = rate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + EncodeText(voiceName) + "\n" + EncodeText(text);
            string output = await RunAsync("synthesize", input, cancellationToken);
            return PcmWaveCodec.Decode(Convert.FromBase64String(output));
        }

        Task<string> RunAsync(string operation, string input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAvailable) throw new PlatformNotSupportedException("Offline speech is available on Windows after building the local speech host. Text input remains available.");
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = new System.Diagnostics.ProcessStartInfo(hostPath, operation)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var process = new System.Diagnostics.Process { StartInfo = start })
                {
                    if (!process.Start()) throw new InvalidOperationException("The local speech host could not start.");
                    using (cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }))
                    {
                        var stdout = process.StandardOutput.ReadToEndAsync();
                        var stderr = process.StandardError.ReadToEndAsync();
                        try
                        {
                            process.StandardInput.WriteLine(input); process.StandardInput.Close();
                            if (!process.WaitForExit(20000)) { process.Kill(); throw new TimeoutException("Local speech took too long. Try again or type your message."); }
                            Task.WaitAll(stdout, stderr);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (process.ExitCode != 0)
                                throw new InvalidOperationException(FriendlyError(stderr.Result.Trim()));
                            string result = stdout.Result.Trim();
                            if (result.Length == 0 || result.Length > 1500000) throw new InvalidDataException("The local speech host returned invalid output.");
                            return result;
                        }
                        catch (IOException) { cancellationToken.ThrowIfCancellationRequested(); throw new InvalidOperationException("The local speech host stopped unexpectedly. Text input remains available."); }
                    }
                }
            }, cancellationToken);
#else
            throw new PlatformNotSupportedException("Offline Windows speech is not available on this platform. Text input remains available.");
#endif
        }

        static string EncodeText(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        static string FriendlyError(string code)
        {
            switch (code)
            {
                case "NO_RECOGNITION": return "No speech was recognized. Try again or type your message.";
                case "NO_ENGLISH_RECOGNIZER": return "No compatible offline English recognizer is installed. Type your message instead.";
                case "NO_ENGLISH_VOICE": case "VOICE_UNAVAILABLE": return "The selected Windows English voice is unavailable. Captions remain available.";
                case "AUDIO_TOO_LONG": return "The generated speech was too long. Captions remain available.";
                default: return "The local Windows speech engine could not complete the request. Text and captions remain available.";
            }
        }
    }
}
