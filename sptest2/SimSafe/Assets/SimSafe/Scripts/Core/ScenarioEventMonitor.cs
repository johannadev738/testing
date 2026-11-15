using System;
using System.Collections.Generic;

namespace SimSafe.Core
{
    /// <summary>Observes existing scenario results without choosing, scoring or modifying scenario state.</summary>
    public sealed class ScenarioEventMonitor
    {
        readonly SimulationEventRecorder recorder;
        ScenarioSession session;
        bool sirenRecorded;

        public ScenarioEventMonitor(SimulationEventRecorder recorder)
        {
            this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        }

        public void Start(ScenarioSession startedSession)
        {
            if (startedSession == null) throw new ArgumentNullException(nameof(startedSession));
            if (!startedSession.IsStarted) throw new InvalidOperationException("Start the scenario before observing it.");
            recorder.StartSession(startedSession.Scenario.id, "officer");
            session = startedSession; sirenRecorded = false;
            session.Changed += ObserveCompletion;
            ObserveCompletion();
        }

        public void ObserveInteraction(DecisionRecord record)
        {
            if (session == null || !recorder.IsActive || sirenRecorded || record == null ||
                !record.IsEnvironmentInteraction || record.OptionId != "car_siren" || !session.HasFlag("sirenOff")) return;
            // Accept only the actual record returned by this session, not a caller-created lookalike.
            bool belongsToSession = false;
            foreach (var decision in session.Decisions)
                if (ReferenceEquals(decision, record)) { belongsToSession = true; break; }
            if (!belongsToSession) return;
            recorder.Record("officer", "patrol", new EnvironmentInteractionPayload("car_siren", false));
            sirenRecorded = true;
        }

        public void End(SessionEndReason reason)
        {
            if (recorder.IsActive) recorder.EndSession(reason);
            if (session != null) session.Changed -= ObserveCompletion;
            session = null;
        }

        public IReadOnlyList<SimulationEvent> Snapshot() => recorder.Snapshot();

        void ObserveCompletion()
        {
            if (session != null && session.IsFinished) End(SessionEndReason.Completed);
        }
    }
}
