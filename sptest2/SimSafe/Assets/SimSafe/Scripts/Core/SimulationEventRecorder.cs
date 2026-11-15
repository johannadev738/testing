using System;
using System.Collections.Generic;

namespace SimSafe.Core
{
    public interface ISimulationClock { double Seconds { get; } }

    /// <summary>Monotonic process clock independent of Unity frame state, timeScale and object teardown.</summary>
    public sealed class StopwatchSimulationClock : ISimulationClock
    {
        readonly System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        public double Seconds => stopwatch.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// Single-threaded session recorder. Use on the simulation thread; marshal provider callbacks there.
    /// Snapshots contain immutable events and remain valid when the next session starts.
    /// No files, network requests, audio, transcript storage or automatic persistence.
    /// </summary>
    public sealed class SimulationEventRecorder
    {
        readonly ISimulationClock clock;
        readonly List<SimulationEvent> events = new List<SimulationEvent>();
        double startedAt, lastClock;
        string scenarioId, sessionActorId;
        public Guid SessionId { get; private set; }
        public bool IsActive { get; private set; }

        public SimulationEventRecorder(ISimulationClock clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public Guid StartSession(string scenarioId, string actorId)
        {
            if (IsActive) throw new InvalidOperationException("End the current session before starting another.");
            EventValidation.Identifier(scenarioId, nameof(scenarioId));
            EventValidation.Identifier(actorId, nameof(actorId));
            double now = ReadClock();
            var id = Guid.NewGuid();
            var first = new SimulationEvent(id, Guid.NewGuid(), scenarioId, actorId, scenarioId, 0,
                SimulationEventType.SessionStarted, new SessionStartedPayload());
            events.Clear(); events.Add(first);
            SessionId = id; this.scenarioId = scenarioId; sessionActorId = actorId;
            startedAt = lastClock = now; IsActive = true;
            return id;
        }

        public SimulationEvent Record(string actorId, string targetId, SimulationEventPayload payload)
        {
            RequireActive();
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Type == SimulationEventType.SessionStarted || payload.Type == SimulationEventType.SessionEnded)
                throw new ArgumentException("Use StartSession/EndSession to record boundaries.", nameof(payload));
            return Append(actorId, targetId, payload);
        }

        public void EndSession(SessionEndReason reason)
        {
            RequireActive();
            Append(sessionActorId, scenarioId, new SessionEndedPayload(reason));
            IsActive = false;
        }

        public IReadOnlyList<SimulationEvent> Snapshot() => Array.AsReadOnly(events.ToArray());

        SimulationEvent Append(string actorId, string targetId, SimulationEventPayload payload)
        {
            double now = ReadClock();
            if (now < lastClock) throw new InvalidOperationException("Simulation clock moved backwards.");
            var value = new SimulationEvent(SessionId, Guid.NewGuid(), scenarioId, actorId, targetId,
                now - startedAt, payload.Type, payload);
            events.Add(value); lastClock = now;
            return value;
        }

        double ReadClock()
        {
            double value = clock.Seconds;
            if (!EventValidation.NonnegativeFinite(value)) throw new InvalidOperationException("Simulation clock must return finite nonnegative seconds.");
            return value;
        }

        void RequireActive()
        {
            if (!IsActive) throw new InvalidOperationException("No recording session is active.");
        }
    }
}
