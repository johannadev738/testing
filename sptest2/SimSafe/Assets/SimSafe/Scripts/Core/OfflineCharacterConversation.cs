using System;
using System.Collections.Generic;

namespace SimSafe.Core
{
    /// <summary>
    /// Single-threaded offline turn scheduler. Samples observed conditions, preserves authored
    /// scenario context and delegates all responses to the existing individual-profile policy.
    /// Typed text restarts a measured processing interval; no language understanding is claimed.
    /// </summary>
    public sealed class OfflineCharacterConversation
    {
        readonly CharacterProfile profile;
        readonly ScenarioCategory category;
        readonly SimulationEventRecorder recorder;
        readonly ISimulationClock clock;
        readonly Guid sessionId;
        readonly double interactionRange;
        readonly OfflineCharacterBehavior behavior = new OfflineCharacterBehavior();
        bool observed, inRange, insideSpace, sirenOn, radioLoud, paused;
        double distance, lastClock, processingSeconds;
        Guid turnId, submissionEventId, proximityEventId, sensoryEventId;
        string officerText;

        public bool CanSubmit => IsCurrentSession && observed && inRange && !paused;
        public bool HasPendingTurn { get; private set; }
        public ResponseIntention CurrentResponse { get; private set; }
        public long ResponseRevision { get; private set; }
        public double SecondsSinceOfficerFinished => processingSeconds;
        public double RemainingWaitSeconds => HasPendingTurn ? Math.Max(0, profile.ProcessingSeconds - processingSeconds) : 0;
        bool IsCurrentSession => recorder.IsActive && recorder.SessionId == sessionId;

        public OfflineCharacterConversation(CharacterProfile profile, ScenarioCategory category,
            SimulationEventRecorder recorder, ISimulationClock clock, double interactionRange = 4.5)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (!Enum.IsDefined(typeof(ScenarioCategory), category)) throw new ArgumentOutOfRangeException(nameof(category));
            if (!EventValidation.NonnegativeFinite(interactionRange) || interactionRange == 0) throw new ArgumentOutOfRangeException(nameof(interactionRange));
            if (!recorder.IsActive) throw new InvalidOperationException("An active recording session is required.");
            this.category = category; this.interactionRange = interactionRange; sessionId = recorder.SessionId;
            lastClock = ReadClock();
        }

        public void Observe(double distanceMetres, bool sirenOn, bool radioLoud, bool paused = false)
        {
            CharacterValidation.Nonnegative(distanceMetres, nameof(distanceMetres));
            if (!IsCurrentSession) { ClearTurn(); return; }
            AdvanceClock();
            bool nextRange = distanceMetres <= interactionRange;
            bool nextSpace = distanceMetres < profile.PersonalSpaceMetres;
            if (!observed || nextRange != inRange || nextSpace != insideSpace)
                proximityEventId = recorder.Record("officer", profile.CharacterId,
                    new OfficerProximityPayload(distanceMetres, nextRange, nextSpace)).EventId;
            if (!observed || this.sirenOn != sirenOn || this.radioLoud != radioLoud)
                sensoryEventId = recorder.Record("scene", profile.CharacterId, new SceneSensoryPayload(sirenOn, radioLoud)).EventId;
            distance = distanceMetres; inRange = nextRange; insideSpace = nextSpace;
            this.sirenOn = sirenOn; this.radioLoud = radioLoud; this.paused = paused; observed = true;
            if (!inRange) { CancelPending(ConversationCancelReason.OutOfRange); return; }
            if (!paused && HasPendingTurn) Evaluate();
        }

        public Guid Submit(string text)
        {
            CharacterValidation.Text(text, nameof(text), false);
            if (!CanSubmit) throw new InvalidOperationException("Conversation requires an active, unpaused officer in interaction range.");
            AdvanceClock();
            CancelPending(ConversationCancelReason.Interrupted);
            turnId = Guid.NewGuid();
            submissionEventId = recorder.Record("officer", profile.CharacterId, new OfficerTextSubmittedPayload(turnId, text.Length)).EventId;
            officerText = text; processingSeconds = 0; HasPendingTurn = true; CurrentResponse = null;
            Evaluate(); return turnId;
        }

        public void Cancel()
        {
            if (IsCurrentSession) { AdvanceClock(); CancelPending(ConversationCancelReason.UserCancelled); }
            else ClearTurn();
        }

        void Evaluate()
        {
            var turn = new CharacterTurn(sessionId, turnId, category, officerText, distance, processingSeconds, sirenOn || radioLoud);
            var response = behavior.Evaluate(profile, turn);
            bool changed = CurrentResponse == null || CurrentResponse.TurnId != response.TurnId ||
                CurrentResponse.Type != response.Type || CurrentResponse.ReasonId != response.ReasonId || CurrentResponse.Text != response.Text;
            if (changed)
            {
                var conditions = recorder.Record("scene", profile.CharacterId,
                    new CharacterConditionsPayload(turnId, category, distance, sirenOn, radioLoud, processingSeconds));
                var evidence = new List<Guid> { submissionEventId, conditions.EventId };
                if (proximityEventId != Guid.Empty) evidence.Add(proximityEventId);
                if (sensoryEventId != Guid.Empty) evidence.Add(sensoryEventId);
                recorder.Record(profile.CharacterId, "officer",
                    new CharacterResponsePayload(turnId, response.Type, response.ReasonId, response.WaitSeconds, evidence.ToArray()));
                ResponseRevision++;
            }
            CurrentResponse = response;
            if (response.Type == ResponseIntentionType.Speak) { HasPendingTurn = false; officerText = null; }
        }

        void CancelPending(ConversationCancelReason reason)
        {
            if (HasPendingTurn) recorder.Record("officer", profile.CharacterId, new CharacterTurnCancelledPayload(turnId, reason));
            ClearTurn();
        }

        void ClearTurn()
        {
            if (CurrentResponse != null) ResponseRevision++;
            HasPendingTurn = false; CurrentResponse = null; officerText = null;
        }

        void AdvanceClock()
        {
            double now = ReadClock();
            if (now < lastClock) throw new InvalidOperationException("Conversation clock moved backwards.");
            // The previous state owns the interval: count up to pause entry, exclude the paused interval on resume.
            if (HasPendingTurn && !paused) processingSeconds += now - lastClock;
            lastClock = now;
        }

        double ReadClock()
        {
            double now = clock.Seconds;
            if (!EventValidation.NonnegativeFinite(now)) throw new InvalidOperationException("Conversation clock must be finite and nonnegative.");
            return now;
        }
    }
}
