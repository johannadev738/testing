using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using SimSafe.Core;

namespace SimSafe
{
    /// <summary>Local Windows speech adapter owned by SimSafeApp. No automatic microphone capture.</summary>
    public sealed class OfflineVoiceController : MonoBehaviour
    {
        [Range(-3, 3)] public int SpeechRate;
        public string VoiceName = "";
        public bool OutputEnabled = true;
        public string Status { get; private set; } = "Voice not initialized.";
        public bool IsRecording => microphoneClip != null;
        public bool IsTranscribing { get; private set; }
        public bool IsRequestingPermission { get; private set; }
        public bool IsAvailable => provider != null && provider.IsAvailable;
        public bool IsSpeaking => playing || readyToPlay || preparing;
        public event Action<string> TranscriptReady;

        WindowsOfflineSpeechProvider provider;
        VoiceSynthesisController synthesis;
        readonly VoiceTurnGate captureGate = new VoiceTurnGate();
        CancellationTokenSource recognitionCancellation;
        SimulationEventRecorder recorder;
        AudioSource speaker;
        AudioClip microphoneClip, replyClip;
        string microphoneDevice;
        Guid captureOperation, captureSession, replyOperation, replyTurn, replySession, confirmedOperation;
        long captureVersion, replyVersion;
        double captureStarted;
        ISimulationClock clock;
        bool paused, playing, readyToPlay, preparing, outputWasEnabled = true;
        int configuredRate;
        string configuredVoice;
        Guid lastTurn;
        string lastReason;

        public void Initialize(Transform character, SimulationEventRecorder eventRecorder, ISimulationClock simulationClock)
        {
            recorder = eventRecorder; clock = simulationClock;
            ConfigureProvider();
            speaker = character.gameObject.AddComponent<AudioSource>();
            speaker.playOnAwake = false; speaker.spatialBlend = 1; speaker.volume = 0.8f;
            speaker.minDistance = 1; speaker.maxDistance = 18; speaker.rolloffMode = AudioRolloffMode.Linear;
            Status = IsAvailable ? "Windows offline voice ready. Record a short message, review, then Send."
                : "Voice unavailable. Build the Windows Offline Host; text input still works.";
        }

        void ConfigureProvider()
        {
            configuredRate = SpeechRate = Mathf.Clamp(SpeechRate, -3, 3);
            configuredVoice = VoiceName ?? "";
            synthesis?.Dispose();
            provider = new WindowsOfflineSpeechProvider(Path.Combine(Application.streamingAssetsPath,
                "SimSafeVoice", "WindowsVoiceHost.exe"), configuredRate, configuredVoice);
            synthesis = new VoiceSynthesisController(provider);
        }

        public void ToggleMicrophone()
        {
            if (!IsAvailable || paused || !recorder.IsActive || IsTranscribing || IsRequestingPermission) return;
            if (IsRecording) FinishCapture();
            else StartCoroutine(BeginCapture());
        }

        IEnumerator BeginCapture()
        {
            CancelCapture(); StopReply();
            captureSession = recorder.SessionId; captureOperation = Guid.NewGuid();
            captureVersion = captureGate.Begin(captureSession, captureOperation);
            long version = captureVersion;
            IsRequestingPermission = true; Status = "Requesting microphone permission...";
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            if (!CaptureCurrent(version)) yield break;
            IsRequestingPermission = false;
            if (paused) yield break;
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            { Status = "Microphone permission denied. You can still type."; yield break; }
            if (Microphone.devices.Length == 0)
            { Status = "No microphone found. You can still type."; yield break; }
            microphoneDevice = Microphone.devices[0];
            try { microphoneClip = Microphone.Start(microphoneDevice, false, 8, 16000); }
            catch (Exception) { Status = "Microphone could not start. Check Windows microphone permissions."; }
            if (microphoneClip == null) { Status = "Microphone did not start. You can still type."; yield break; }
            captureStarted = clock.Seconds; Status = "Recording locally (maximum 8 seconds). Click Stop when finished.";
            ObserveVoice(captureSession, captureOperation, null, VoiceActivityKind.MicrophoneStarted);
        }

        async void FinishCapture()
        {
            if (!IsRecording) return;
            long version = captureVersion;
            AudioClip clip = microphoneClip;
            int frames = Microphone.GetPosition(microphoneDevice);
            if (frames <= 0 && clock.Seconds - captureStarted >= 7.9) frames = clip.samples;
            frames = Mathf.Clamp(frames, 0, clip.samples);
            Microphone.End(microphoneDevice); microphoneClip = null;
            ObserveVoice(captureSession, captureOperation, null, VoiceActivityKind.MicrophoneStopped);
            try
            {
                if (frames <= 0) { Status = "No recorded audio. Try again or type."; return; }
                var samples = new float[frames * clip.channels];
                if (!clip.GetData(samples, 0)) { Status = "Recorded audio could not be read. You can still type."; return; }
                var pcm = new PcmAudio(samples, clip.frequency, clip.channels);
                recognitionCancellation = new CancellationTokenSource();
                IsTranscribing = true; Status = "Transcribing locally. Review the result before sending.";
                string text = await provider.TranscribeAsync(pcm, recognitionCancellation.Token);
                if (this == null || !CaptureCurrent(version) || paused) return;
                if (string.IsNullOrWhiteSpace(text) || text.Length > 512)
                { Status = "No usable transcript. Try a short message or type."; return; }
                confirmedOperation = captureOperation;
                ObserveVoice(captureSession, captureOperation, null, VoiceActivityKind.TranscriptReady);
                TranscriptReady?.Invoke(text);
                Status = "Transcript ready. Check/edit it, then Send. Nothing has been submitted yet.";
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (this != null && CaptureCurrent(version))
                { Status = "Offline recognition failed. You can still type."; ObserveVoice(captureSession, captureOperation, null, VoiceActivityKind.Failed, "recognition"); }
            }
            finally
            {
                if (clip != null) Destroy(clip);
                if (this != null && version == captureVersion) IsTranscribing = false;
            }
        }

        public void ConfirmTranscript(Guid turnId)
        {
            if (confirmedOperation == Guid.Empty || captureSession != recorder.SessionId) return;
            ObserveVoice(captureSession, confirmedOperation, turnId, VoiceActivityKind.TranscriptConfirmed);
            confirmedOperation = Guid.Empty;
        }

        public void CancelCapture()
        {
            bool active = IsRecording || IsTranscribing || IsRequestingPermission;
            captureGate.Cancel(); captureVersion++;
            recognitionCancellation?.Cancel(); recognitionCancellation?.Dispose(); recognitionCancellation = null;
            if (IsRecording)
            {
                Microphone.End(microphoneDevice); Destroy(microphoneClip); microphoneClip = null;
                ObserveVoice(captureSession, captureOperation, null, VoiceActivityKind.MicrophoneStopped, "cancelled");
            }
            IsTranscribing = IsRequestingPermission = false; confirmedOperation = Guid.Empty;
            if (active) Status = "Microphone input cancelled. Text input remains available.";
        }

        bool CaptureCurrent(long version) => recorder != null && recorder.IsActive && recorder.SessionId == captureSession &&
            captureGate.IsCurrent(version, captureSession, captureOperation);

        public async void Present(ResponseIntention response)
        {
            if (response == null || response.Type == ResponseIntentionType.Wait)
            { StopReply(); return; }
            if (!IsAvailable || !OutputEnabled || paused || IsRecording || IsTranscribing || IsRequestingPermission) return;
            if (lastTurn == response.TurnId && lastReason == response.ReasonId) return;
            StopReply(); lastTurn = response.TurnId; lastReason = response.ReasonId;
            if (SpeechRate != configuredRate || (VoiceName ?? "") != configuredVoice) ConfigureProvider();
            long version = replyVersion;
            replySession = response.SessionId; replyTurn = response.TurnId; replyOperation = Guid.NewGuid();
            Status = "Preparing Sam's offline voice...";
            preparing = true;
            try
            {
                PcmAudio pcm = await synthesis.SynthesizeAsync(response, CancellationToken.None);
                if (this == null || pcm == null || version != replyVersion || !ReplyCurrent()) return;
                replyClip = AudioClip.Create("Sam offline reply", pcm.SampleCount / pcm.Channels, pcm.Channels, pcm.SampleRate, false);
                if (!replyClip.SetData(pcm.CopySamples(), 0)) throw new InvalidOperationException("Invalid voice audio.");
                speaker.clip = replyClip; readyToPlay = true;
                if (!paused) PlayReadyReply();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (this != null && version == replyVersion)
                { Status = "Offline speech unavailable. Read Sam's caption."; ObserveVoice(replySession, replyOperation, replyTurn, VoiceActivityKind.Failed, "synthesis"); }
            }
            finally { if (this != null && version == replyVersion) preparing = false; }
        }

        bool ReplyCurrent() => recorder != null && recorder.IsActive && recorder.SessionId == replySession && OutputEnabled;

        void PlayReadyReply()
        {
            if (!readyToPlay || !ReplyCurrent() || speaker == null) return;
            readyToPlay = false; speaker.Play(); playing = speaker.isPlaying;
            if (playing)
            { Status = "Sam is speaking (Windows offline voice)."; ObserveVoice(replySession, replyOperation, replyTurn, VoiceActivityKind.ReplyStarted); }
            else Status = "Voice playback did not start. Read Sam's caption.";
        }

        public void SetPaused(bool value)
        {
            paused = value;
            if (value) { CancelCapture(); if (playing && speaker != null) speaker.Pause(); }
            else if (playing && speaker != null) speaker.UnPause();
            else if (readyToPlay) PlayReadyReply();
        }

        public void SetOutputEnabled(bool value)
        {
            OutputEnabled = value;
            outputWasEnabled = value;
            if (!value) StopReply();
        }

        void StopReply()
        {
            replyVersion++; synthesis?.Cancel();
            if (playing) ObserveVoice(replySession, replyOperation, replyTurn, VoiceActivityKind.ReplyCancelled);
            playing = readyToPlay = preparing = false;
            if (speaker != null) { speaker.Stop(); speaker.clip = null; }
            if (replyClip != null) { Destroy(replyClip); replyClip = null; }
            lastTurn = Guid.Empty; lastReason = null;
        }

        public void CancelAll() { CancelCapture(); StopReply(); paused = false; }

        void Update()
        {
            if (OutputEnabled != outputWasEnabled) SetOutputEnabled(OutputEnabled);
            if (IsRecording && clock.Seconds - captureStarted >= 8) FinishCapture();
            if (playing && !paused && speaker != null && !speaker.isPlaying)
            {
                playing = false; Status = "Sam finished speaking.";
                ObserveVoice(replySession, replyOperation, replyTurn, VoiceActivityKind.ReplyFinished);
                speaker.clip = null;
                if (replyClip != null) { Destroy(replyClip); replyClip = null; }
            }
        }

        void ObserveVoice(Guid sessionId, Guid operationId, Guid? turnId, VoiceActivityKind kind, string detail = "none")
        {
            if (recorder != null && recorder.IsActive && recorder.SessionId == sessionId && operationId != Guid.Empty)
                recorder.Record(kind == VoiceActivityKind.MicrophoneStarted || kind == VoiceActivityKind.MicrophoneStopped ||
                    kind == VoiceActivityKind.TranscriptReady || kind == VoiceActivityKind.TranscriptConfirmed ||
                    (kind == VoiceActivityKind.Failed && detail == "recognition") ? "officer" : "sam",
                    "voice", new VoiceActivityPayload(operationId, turnId, kind, detail));
        }

        void OnDestroy() { CancelAll(); synthesis?.Dispose(); }
    }
}
