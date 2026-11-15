using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimSafe.Core
{
    /// <summary>Authored dispatch/context metadata, never an inferred diagnosis.</summary>
    public enum ScenarioCategory { UAI, KAI }
    public enum ResponseIntentionType { Speak, Wait, RequestSpace, Gesture, MoveAway }

    /// <summary>
    /// One fictional individual's provisional, authored responses. Not a universal behavior model
    /// or NASC-approved content. Runtime profiles do not replace existing scenario resources.
    /// </summary>
    public sealed class CharacterProfile
    {
        public string CharacterId { get; }
        public double ProcessingSeconds { get; }
        public double PersonalSpaceMetres { get; }
        public string QuietReply { get; }
        public string NoisyReply { get; }
        public string SpaceRequest { get; }

        public CharacterProfile(string characterId, double processingSeconds, double personalSpaceMetres,
            string quietReply, string noisyReply, string spaceRequest)
        {
            CharacterId = EventValidation.Identifier(characterId, nameof(characterId));
            ProcessingSeconds = CharacterValidation.Nonnegative(processingSeconds, nameof(processingSeconds));
            PersonalSpaceMetres = CharacterValidation.Nonnegative(personalSpaceMetres, nameof(personalSpaceMetres));
            QuietReply = CharacterValidation.Text(quietReply, nameof(quietReply), false);
            NoisyReply = CharacterValidation.Text(noisyReply, nameof(noisyReply), false);
            SpaceRequest = CharacterValidation.Text(spaceRequest, nameof(spaceRequest), false);
        }
    }

    /// <summary>
    /// Ephemeral text-test observation. Caller supplies measured distance/wait time and authored
    /// scenario category. It is not automatically persisted or added to training evidence.
    /// </summary>
    public sealed class CharacterTurn
    {
        public Guid SessionId { get; }
        public Guid TurnId { get; }
        public ScenarioCategory Category { get; }
        public string OfficerText { get; }
        public double DistanceMetres { get; }
        public double SecondsSinceOfficerFinished { get; }
        public bool SensoryInputActive { get; }

        public CharacterTurn(Guid sessionId, Guid turnId, ScenarioCategory category, string officerText,
            double distanceMetres, double secondsSinceOfficerFinished, bool sensoryInputActive)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Session ID is required.", nameof(sessionId));
            if (turnId == Guid.Empty) throw new ArgumentException("Turn ID is required.", nameof(turnId));
            if (!Enum.IsDefined(typeof(ScenarioCategory), category)) throw new ArgumentOutOfRangeException(nameof(category));
            OfficerText = CharacterValidation.Text(officerText, nameof(officerText), false);
            DistanceMetres = CharacterValidation.Nonnegative(distanceMetres, nameof(distanceMetres));
            SecondsSinceOfficerFinished = CharacterValidation.Nonnegative(secondsSinceOfficerFinished, nameof(secondsSinceOfficerFinished));
            SessionId = sessionId; TurnId = turnId; Category = category; SensoryInputActive = sensoryInputActive;
        }
    }

    /// <summary>A bounded presentation suggestion, not authorization to mutate scene state or scoring.</summary>
    public sealed class ResponseIntention
    {
        public Guid SessionId { get; }
        public Guid TurnId { get; }
        public ResponseIntentionType Type { get; }
        public string ReasonId { get; }
        public string Text { get; }
        public double WaitSeconds { get; }

        public ResponseIntention(CharacterTurn turn, ResponseIntentionType type, string reasonId, string text, double waitSeconds = 0)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (!Enum.IsDefined(typeof(ResponseIntentionType), type)) throw new ArgumentOutOfRangeException(nameof(type));
            ReasonId = EventValidation.Identifier(reasonId, nameof(reasonId));
            Text = CharacterValidation.Text(text, nameof(text), type != ResponseIntentionType.Speak);
            WaitSeconds = CharacterValidation.Nonnegative(waitSeconds, nameof(waitSeconds));
            if (type != ResponseIntentionType.Wait && waitSeconds != 0)
                throw new ArgumentException("Only a wait intention can specify a processing interval.", nameof(waitSeconds));
            SessionId = turn.SessionId; TurnId = turn.TurnId; Type = type;
        }

        public bool IsFor(CharacterTurn turn) => turn != null && turn.SessionId == SessionId && turn.TurnId == TurnId;
    }

    public interface ICharacterBehavior
    {
        ResponseIntention Evaluate(CharacterProfile profile, CharacterTurn turn);
    }

    /// <summary>
    /// Stateless offline demonstration only. No autism classification, speech interpretation,
    /// scoring or autonomous scene actions. Profiles deliberately control all thresholds/lines.
    /// </summary>
    public sealed class OfflineCharacterBehavior : ICharacterBehavior
    {
        public ResponseIntention Evaluate(CharacterProfile profile, CharacterTurn turn)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (turn.DistanceMetres < profile.PersonalSpaceMetres)
                return new ResponseIntention(turn, ResponseIntentionType.RequestSpace, "profile.personal_space", profile.SpaceRequest);
            if (turn.SecondsSinceOfficerFinished < profile.ProcessingSeconds)
                return new ResponseIntention(turn, ResponseIntentionType.Wait, "profile.processing", "",
                    profile.ProcessingSeconds - turn.SecondsSinceOfficerFinished);
            return new ResponseIntention(turn, ResponseIntentionType.Speak,
                turn.SensoryInputActive ? "profile.sensory" : "profile.ready",
                turn.SensoryInputActive ? profile.NoisyReply : profile.QuietReply);
        }
    }

    /// <summary>
    /// Short-lived PCM transport. Samples are normalized interleaved floats; no disk persistence.
    /// Each provider invocation belongs to a caller-held session/turn and cancellation token.
    /// </summary>
    public sealed class PcmAudio
    {
        readonly float[] samples;
        public int SampleRate { get; }
        public int Channels { get; }
        public int SampleCount => samples.Length;

        public PcmAudio(float[] samples, int sampleRate, int channels)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (sampleRate < 8000 || sampleRate > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels < 1 || channels > 2) throw new ArgumentOutOfRangeException(nameof(channels));
            if (samples.Length == 0 || samples.Length % channels != 0 || samples.Length > (long)sampleRate * channels * 30)
                throw new ArgumentException("PCM must contain complete frames and at most 30 seconds.", nameof(samples));
            this.samples = (float[])samples.Clone();
            foreach (float value in this.samples)
                if (float.IsNaN(value) || float.IsInfinity(value) || value < -1 || value > 1)
                    throw new ArgumentException("PCM samples must be finite and normalized.", nameof(samples));
            SampleRate = sampleRate; Channels = channels;
        }

        public float[] CopySamples() => (float[])samples.Clone();
    }

    // Provider implementations must not log/persist identifiable audio or transcripts by default.
    // Callers must cancel obsolete turns and discard results whose session/turn is no longer active.
    // Unity audio playback/main-thread marshalling belongs to a future runtime adapter.
    public interface ISpeechToTextProvider
    {
        Task<string> TranscribeAsync(PcmAudio audio, CancellationToken cancellationToken);
    }
    public interface IDialogueProvider
    {
        Task<string> GenerateAsync(CharacterTurn turn, ResponseIntention intention, CancellationToken cancellationToken);
    }
    public interface ITextToSpeechProvider
    {
        Task<PcmAudio> SynthesizeAsync(string text, CancellationToken cancellationToken);
    }
    public interface ILipSyncDriver
    {
        Task PlayAsync(PcmAudio audio, CancellationToken cancellationToken);
    }

    /// <summary>Deterministically presents authored intention text; does not call any AI service.</summary>
    public sealed class OfflineDialogueProvider : IDialogueProvider
    {
        public Task<string> GenerateAsync(CharacterTurn turn, ResponseIntention intention, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (intention == null) throw new ArgumentNullException(nameof(intention));
            if (!intention.IsFor(turn)) throw new ArgumentException("Intention belongs to another session or turn.", nameof(intention));
            return Task.FromResult(intention.Text);
        }
    }

    internal static class CharacterValidation
    {
        internal static double Nonnegative(double value, string name)
        {
            if (!EventValidation.NonnegativeFinite(value)) throw new ArgumentOutOfRangeException(name);
            return value;
        }
        internal static string Text(string value, string name, bool allowEmpty)
        {
            if (value == null || value.Length > 512 || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
                throw new ArgumentException("Text must be bounded to 512 characters and present when required.", name);
            return value;
        }
    }
}
