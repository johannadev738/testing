using System;

namespace SimSafe.Core
{
    public enum SimulationEventType
    {
        SessionStarted, SessionEnded, EnvironmentInteractionSucceeded,
        OfficerProximityChanged, SceneSensoryChanged, OfficerTextSubmitted,
        CharacterConditionsObserved, CharacterResponseProduced, CharacterTurnCancelled,
        VoiceActivityObserved
    }
    public enum SessionEndReason { Completed, Restarted, ReturnedToMenu, ApplicationStopped }

    /// <summary>Closed payload hierarchy: extend with sealed, validated payloads, never arbitrary objects.</summary>
    public abstract class SimulationEventPayload
    {
        internal SimulationEventPayload() { }
        public abstract SimulationEventType Type { get; }
    }

    public sealed class SessionStartedPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.SessionStarted;
    }

    public sealed class SessionEndedPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.SessionEnded;
        public SessionEndReason Reason { get; }
        public SessionEndedPayload(SessionEndReason reason)
        {
            if (!Enum.IsDefined(typeof(SessionEndReason), reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }
    }

    /// <summary>Metadata only: identifies a verified control change, with no dialogue or raw audio.</summary>
    public sealed class EnvironmentInteractionPayload : SimulationEventPayload
    {
        public override SimulationEventType Type => SimulationEventType.EnvironmentInteractionSucceeded;
        public string InteractionId { get; }
        public bool Enabled { get; }
        public EnvironmentInteractionPayload(string interactionId, bool enabled)
        {
            InteractionId = EventValidation.Identifier(interactionId, nameof(interactionId));
            Enabled = enabled;
        }
    }

    /// <summary>Immutable observed event. Elapsed time is nonnegative real time, not wall-clock time.</summary>
    public sealed class SimulationEvent
    {
        public Guid SessionId { get; }
        public Guid EventId { get; }
        public string ScenarioId { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public double ElapsedSeconds { get; }
        public SimulationEventType Type { get; }
        public SimulationEventPayload Payload { get; }

        public SimulationEvent(Guid sessionId, Guid eventId, string scenarioId, string actorId,
            string targetId, double elapsedSeconds, SimulationEventType type, SimulationEventPayload payload)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("A session ID is required.", nameof(sessionId));
            if (eventId == Guid.Empty) throw new ArgumentException("An event ID is required.", nameof(eventId));
            if (!EventValidation.NonnegativeFinite(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!Enum.IsDefined(typeof(SimulationEventType), type)) throw new ArgumentOutOfRangeException(nameof(type));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Type != type) throw new ArgumentException("Payload does not match event type.", nameof(payload));
            ScenarioId = EventValidation.Identifier(scenarioId, nameof(scenarioId));
            ActorId = EventValidation.Identifier(actorId, nameof(actorId));
            TargetId = EventValidation.Identifier(targetId, nameof(targetId));
            SessionId = sessionId; EventId = eventId; ElapsedSeconds = elapsedSeconds;
            Type = type; Payload = payload;
        }
    }

    internal static class EventValidation
    {
        // Identifiers are metadata keys, not places to store free-form utterances or personal information.
        internal static string Identifier(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                throw new ArgumentException("Identifier must contain 1 to 128 characters.", name);
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.' || c == ':'))
                    throw new ArgumentException("Identifier contains unsupported characters.", name);
            return value;
        }
        internal static bool NonnegativeFinite(double value) => value >= 0 && !double.IsInfinity(value) && !double.IsNaN(value);
    }
}
