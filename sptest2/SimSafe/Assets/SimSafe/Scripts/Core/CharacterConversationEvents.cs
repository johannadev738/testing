using System;
using System.Collections.Generic;

namespace SimSafe.Core
{
    public enum ConversationCancelReason { UserCancelled, Interrupted, OutOfRange }

    public sealed class OfficerProximityPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.OfficerProximityChanged;
        public double DistanceMetres { get; }
        public bool InInteractionRange { get; }
        public bool InsidePersonalSpace { get; }
        public OfficerProximityPayload(double distanceMetres, bool inInteractionRange, bool insidePersonalSpace)
        {
            DistanceMetres = CharacterValidation.Nonnegative(distanceMetres, nameof(distanceMetres));
            InInteractionRange = inInteractionRange; InsidePersonalSpace = insidePersonalSpace;
        }
    }

    public sealed class SceneSensoryPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.SceneSensoryChanged;
        public bool SirenOn { get; }
        public bool RadioLoud { get; }
        public SceneSensoryPayload(bool sirenOn, bool radioLoud) { SirenOn = sirenOn; RadioLoud = radioLoud; }
    }

    /// <summary>Typed input was submitted; the transcript itself is deliberately not retained.</summary>
    public sealed class OfficerTextSubmittedPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.OfficerTextSubmitted;
        public Guid TurnId { get; }
        public int CharacterCount { get; }
        public OfficerTextSubmittedPayload(Guid turnId, int characterCount)
        {
            TurnId = ConversationValidation.Id(turnId, nameof(turnId));
            if (characterCount < 1 || characterCount > 512) throw new ArgumentOutOfRangeException(nameof(characterCount));
            CharacterCount = characterCount;
        }
    }

    /// <summary>Exact conditions used for one response; no diagnosis or inferred medical classification.</summary>
    public sealed class CharacterConditionsPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.CharacterConditionsObserved;
        public Guid TurnId { get; }
        public ScenarioCategory Category { get; }
        public double DistanceMetres { get; }
        public bool SirenOn { get; }
        public bool RadioLoud { get; }
        public double ProcessingSeconds { get; }
        public CharacterConditionsPayload(Guid turnId, ScenarioCategory category, double distanceMetres,
            bool sirenOn, bool radioLoud, double processingSeconds)
        {
            TurnId = ConversationValidation.Id(turnId, nameof(turnId));
            if (!Enum.IsDefined(typeof(ScenarioCategory), category)) throw new ArgumentOutOfRangeException(nameof(category));
            Category = category; DistanceMetres = CharacterValidation.Nonnegative(distanceMetres, nameof(distanceMetres));
            ProcessingSeconds = CharacterValidation.Nonnegative(processingSeconds, nameof(processingSeconds));
            SirenOn = sirenOn; RadioLoud = radioLoud;
        }
    }

    /// <summary>Policy produced an intention. This is not proof of speech audio or executed animation.</summary>
    public sealed class CharacterResponsePayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.CharacterResponseProduced;
        public Guid TurnId { get; }
        public ResponseIntentionType Intention { get; }
        public string ReasonId { get; }
        public double WaitSeconds { get; }
        public IReadOnlyList<Guid> EvidenceEventIds { get; }
        public CharacterResponsePayload(Guid turnId, ResponseIntentionType intention, string reasonId,
            double waitSeconds, Guid[] evidenceEventIds)
        {
            TurnId = ConversationValidation.Id(turnId, nameof(turnId));
            if (!Enum.IsDefined(typeof(ResponseIntentionType), intention)) throw new ArgumentOutOfRangeException(nameof(intention));
            ReasonId = EventValidation.Identifier(reasonId, nameof(reasonId));
            WaitSeconds = CharacterValidation.Nonnegative(waitSeconds, nameof(waitSeconds));
            if (intention != ResponseIntentionType.Wait && waitSeconds != 0)
                throw new ArgumentException("Only WAIT has a remaining interval.", nameof(waitSeconds));
            if (evidenceEventIds == null) throw new ArgumentNullException(nameof(evidenceEventIds));
            if (evidenceEventIds.Length < 2 || evidenceEventIds.Length > 8)
                throw new ArgumentException("A response needs two to eight evidence references.", nameof(evidenceEventIds));
            var copy = (Guid[])evidenceEventIds.Clone();
            var unique = new HashSet<Guid>();
            foreach (var id in copy)
                if (id == Guid.Empty || !unique.Add(id)) throw new ArgumentException("Evidence IDs must be nonempty and unique.", nameof(evidenceEventIds));
            EvidenceEventIds = Array.AsReadOnly(copy); Intention = intention;
        }
    }

    public sealed class CharacterTurnCancelledPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.CharacterTurnCancelled;
        public Guid TurnId { get; }
        public ConversationCancelReason Reason { get; }
        public CharacterTurnCancelledPayload(Guid turnId, ConversationCancelReason reason)
        {
            TurnId = ConversationValidation.Id(turnId, nameof(turnId));
            if (!Enum.IsDefined(typeof(ConversationCancelReason), reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }
    }

    internal static class ConversationValidation
    {
        internal static Guid Id(Guid value, string name)
        {
            if (value == Guid.Empty) throw new ArgumentException("A nonempty identifier is required.", name);
            return value;
        }
    }
}
