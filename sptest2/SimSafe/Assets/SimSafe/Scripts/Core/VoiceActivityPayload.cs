using System;

namespace SimSafe.Core
{
    public enum VoiceActivityKind
    {
        MicrophoneStarted, MicrophoneStopped, TranscriptReady,
        ReplyStarted, ReplyFinished, ReplyCancelled, Failed, TranscriptConfirmed
    }

    /// <summary>Observed transport/playback metadata only; never raw audio, transcript or exception text.</summary>
    public sealed class VoiceActivityPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.VoiceActivityObserved;
        public Guid OperationId { get; }
        public Guid? TurnId { get; }
        public VoiceActivityKind Kind { get; }
        public string DetailId { get; }

        public VoiceActivityPayload(Guid operationId, Guid? turnId, VoiceActivityKind kind, string detailId = "none")
        {
            if (operationId == Guid.Empty) throw new ArgumentException("Voice activity requires an operation ID.", nameof(operationId));
            if (turnId.HasValue && turnId.Value == Guid.Empty) throw new ArgumentException("Turn ID must be nonempty when present.", nameof(turnId));
            if (!Enum.IsDefined(typeof(VoiceActivityKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            DetailId = EventValidation.Identifier(detailId, nameof(detailId));
            OperationId = operationId; TurnId = turnId; Kind = kind;
        }
    }
}
