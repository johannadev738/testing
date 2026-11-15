using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SimSafe.Core;

namespace SimSafe.Tests
{
    [TestFixture]
    public sealed class OfflineCharacterConversationTests
    {
        sealed class Clock : ISimulationClock { public double Seconds { get; set; } }
        Clock clock;
        SimulationEventRecorder recorder;
        OfflineCharacterConversation conversation;
        CharacterProfile profile;

        [SetUp] public void SetUp()
        {
            clock = new Clock(); recorder = new SimulationEventRecorder(clock);
            recorder.StartSession("welfare", "officer");
            profile = new CharacterProfile("sam", 12, 2, "I can listen.", "Please lower the noise.", "Please give me space.");
            conversation = new OfflineCharacterConversation(profile, ScenarioCategory.KAI, recorder, clock);
        }

        [Test] public void RangeAndPersonalSpaceCrossingsAreRecordedWithoutFrameSpam()
        {
            conversation.Observe(5, true, true); Assert.IsFalse(conversation.CanSubmit);
            int initial = recorder.Snapshot().Count;
            conversation.Observe(5.1, true, true); Assert.AreEqual(initial, recorder.Snapshot().Count);
            conversation.Observe(4.5, true, true); Assert.IsTrue(conversation.CanSubmit);
            conversation.Observe(1.9, true, true);
            var proximity = recorder.Snapshot().Where(e => e.Type == SimulationEventType.OfficerProximityChanged).ToArray();
            Assert.AreEqual(3, proximity.Length);
            Assert.IsTrue(((OfficerProximityPayload)proximity[2].Payload).InsidePersonalSpace);
        }

        [Test] public void SubmissionStartsMeasuredWaitAndRepliesAtExactThreshold()
        {
            conversation.Observe(3, false, false); var id = conversation.Submit("Hello");
            Assert.AreEqual(ResponseIntentionType.Wait, conversation.CurrentResponse.Type);
            Assert.AreEqual(id, conversation.CurrentResponse.TurnId);
            clock.Seconds = 11.99; conversation.Observe(3, false, false);
            Assert.AreEqual(ResponseIntentionType.Wait, conversation.CurrentResponse.Type);
            clock.Seconds = 12; conversation.Observe(3, false, false);
            Assert.AreEqual(ResponseIntentionType.Speak, conversation.CurrentResponse.Type);
            Assert.AreEqual("I can listen.", conversation.CurrentResponse.Text);
            Assert.IsFalse(conversation.HasPendingTurn);
        }

        [Test] public void WaitCountdownDoesNotGenerateResponsesEveryFrame()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            long revision = conversation.ResponseRevision;
            clock.Seconds = 5; conversation.Observe(3, false, false);
            Assert.AreEqual(7d, conversation.RemainingWaitSeconds);
            Assert.AreEqual(revision, conversation.ResponseRevision);
            Assert.AreEqual(1, recorder.Snapshot().Count(e => e.Type == SimulationEventType.CharacterResponseProduced));
        }

        [Test] public void PersonalSpaceRequestsRetainTurnUntilSpaceAndWaitAreSatisfied()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            clock.Seconds = 2; conversation.Observe(1, false, false);
            Assert.AreEqual(ResponseIntentionType.RequestSpace, conversation.CurrentResponse.Type);
            Assert.IsTrue(conversation.HasPendingTurn);
            clock.Seconds = 3; conversation.Observe(2, false, false);
            Assert.AreEqual(ResponseIntentionType.Wait, conversation.CurrentResponse.Type);
            clock.Seconds = 12; conversation.Observe(2, false, false);
            Assert.AreEqual(ResponseIntentionType.Speak, conversation.CurrentResponse.Type);
        }

        [Test] public void BothSirenAndRadioInfluenceRecordedResponseConditions()
        {
            conversation.Observe(3, false, true); conversation.Submit("Hello");
            clock.Seconds = 12; conversation.Observe(3, false, true);
            Assert.AreEqual("Please lower the noise.", conversation.CurrentResponse.Text);
            var conditions = recorder.Snapshot().Select(e => e.Payload).OfType<CharacterConditionsPayload>().Last();
            Assert.IsFalse(conditions.SirenOn); Assert.IsTrue(conditions.RadioLoud);
            conversation.Observe(3, true, false); conversation.Submit("Hello again");
            clock.Seconds = 24; conversation.Observe(3, true, false);
            Assert.AreEqual("Please lower the noise.", conversation.CurrentResponse.Text);
        }

        [Test] public void SensoryChangesDuringWaitAffectTheFinalReply()
        {
            conversation.Observe(3, true, true); conversation.Submit("Hello");
            clock.Seconds = 6; conversation.Observe(3, false, false);
            clock.Seconds = 12; conversation.Observe(3, false, false);
            Assert.AreEqual("I can listen.", conversation.CurrentResponse.Text);
            Assert.AreEqual(2, recorder.Snapshot().Count(e => e.Type == SimulationEventType.SceneSensoryChanged));
        }

        [Test] public void PauseFreezesProcessingAndCannotSubmitOrProduceAResponse()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            clock.Seconds = 4; conversation.Observe(3, false, false, true);
            clock.Seconds = 100; conversation.Observe(3, false, false, true);
            Assert.IsFalse(conversation.CanSubmit);
            Assert.Throws<InvalidOperationException>(() => conversation.Submit("paused"));
            Assert.AreEqual(4d, conversation.SecondsSinceOfficerFinished);
            conversation.Observe(3, false, false);
            Assert.AreEqual(4d, conversation.SecondsSinceOfficerFinished);
            clock.Seconds = 108; conversation.Observe(3, false, false);
            Assert.AreEqual(ResponseIntentionType.Speak, conversation.CurrentResponse.Type);
        }

        [Test] public void NewSubmissionInterruptsAndRestartsTheProcessingInterval()
        {
            conversation.Observe(3, false, false); var first = conversation.Submit("Hello");
            clock.Seconds = 10; conversation.Observe(3, false, false);
            var second = conversation.Submit("Take your time");
            Assert.IsTrue(first != second);
            var cancelled = recorder.Snapshot().Select(e => e.Payload).OfType<CharacterTurnCancelledPayload>().Single();
            Assert.AreEqual(first, cancelled.TurnId); Assert.AreEqual(ConversationCancelReason.Interrupted, cancelled.Reason);
            clock.Seconds = 21; conversation.Observe(3, false, false);
            Assert.AreEqual(ResponseIntentionType.Wait, conversation.CurrentResponse.Type);
            clock.Seconds = 22; conversation.Observe(3, false, false);
            Assert.AreEqual(ResponseIntentionType.Speak, conversation.CurrentResponse.Type);
        }

        [Test] public void LeavingRangeCancelsBeforeAnOverdueReply()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            clock.Seconds = 15; conversation.Observe(4.6, false, false);
            Assert.IsFalse(conversation.HasPendingTurn); Assert.IsNull(conversation.CurrentResponse);
            Assert.AreEqual(ConversationCancelReason.OutOfRange,
                recorder.Snapshot().Select(e => e.Payload).OfType<CharacterTurnCancelledPayload>().Last().Reason);
            conversation.Observe(3, false, false);
            Assert.IsNull(conversation.CurrentResponse); Assert.IsTrue(conversation.CanSubmit);
        }

        [Test] public void ExplicitCancelIsIdempotentAndClearsPendingResponse()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            conversation.Cancel(); conversation.Cancel();
            Assert.IsFalse(conversation.HasPendingTurn); Assert.IsNull(conversation.CurrentResponse);
            Assert.AreEqual(1, recorder.Snapshot().Count(e => e.Type == SimulationEventType.CharacterTurnCancelled));
        }

        [Test] public void ResponsesReferenceEarlierSameSessionSubmissionAndConditions()
        {
            conversation.Observe(3, false, false); var turnId = conversation.Submit("Hello");
            clock.Seconds = 12; conversation.Observe(3, false, false);
            var events = recorder.Snapshot();
            foreach (var responseEvent in events.Where(e => e.Type == SimulationEventType.CharacterResponseProduced))
            {
                var payload = (CharacterResponsePayload)responseEvent.Payload;
                Assert.AreEqual(turnId, payload.TurnId);
                foreach (Guid evidenceId in payload.EvidenceEventIds)
                {
                    var evidence = events.Single(e => e.EventId == evidenceId);
                    Assert.AreEqual(responseEvent.SessionId, evidence.SessionId);
                    Assert.LessOrEqual(evidence.ElapsedSeconds, responseEvent.ElapsedSeconds);
                }
                Assert.IsTrue(payload.EvidenceEventIds.Any(id => events.Single(e => e.EventId == id).Type == SimulationEventType.OfficerTextSubmitted));
                Assert.IsTrue(payload.EvidenceEventIds.Any(id => events.Single(e => e.EventId == id).Type == SimulationEventType.CharacterConditionsObserved));
            }
        }

        [Test] public void EvidenceIdentifiersAreDefensivelyCopiedAndReadOnly()
        {
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var payload = new CharacterResponsePayload(Guid.NewGuid(), ResponseIntentionType.Wait, "profile.processing", 12, ids);
            var first = ids[0]; ids[0] = Guid.NewGuid(); Assert.AreEqual(first, payload.EvidenceEventIds[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<Guid>)payload.EvidenceEventIds).Clear());
        }

        [Test] public void TypedTextIsMetadataOnlyAndCategoryRemainsAuthored()
        {
            conversation.Observe(3, false, false); conversation.Submit("This person might be autistic");
            var speech = recorder.Snapshot().Select(e => e.Payload).OfType<OfficerTextSubmittedPayload>().Single();
            Assert.AreEqual(29, speech.CharacterCount);
            var conditions = recorder.Snapshot().Select(e => e.Payload).OfType<CharacterConditionsPayload>().Single();
            Assert.AreEqual(ScenarioCategory.KAI, conditions.Category);
            Assert.IsFalse(typeof(OfficerTextSubmittedPayload).GetProperties().Any(p => p.PropertyType == typeof(string)));
        }

        [Test] public void EndedOrReplacedSessionCannotReceiveLateConversationEvents()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            recorder.EndSession(SessionEndReason.Restarted); recorder.StartSession("next", "officer");
            clock.Seconds = 20; conversation.Observe(3, false, false); conversation.Cancel();
            Assert.IsFalse(conversation.CanSubmit); Assert.IsFalse(conversation.HasPendingTurn);
            Assert.Throws<InvalidOperationException>(() => conversation.Submit("stale"));
            Assert.AreEqual(1, recorder.Snapshot().Count);
        }

        [Test] public void InvalidInputDoesNotInterruptTheExistingTurn()
        {
            conversation.Observe(3, false, false); var first = conversation.Submit("Hello");
            int count = recorder.Snapshot().Count;
            Assert.Throws<ArgumentException>(() => conversation.Submit(" "));
            Assert.Throws<ArgumentException>(() => conversation.Submit(new string('x', 513)));
            Assert.AreEqual(first, conversation.CurrentResponse.TurnId); Assert.AreEqual(count, recorder.Snapshot().Count);
        }

        [Test] public void RegressingClockAndInvalidDistancesAreRejectedBeforeWrites()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            clock.Seconds = 5; conversation.Observe(3, false, false); int count = recorder.Snapshot().Count;
            clock.Seconds = 4; Assert.Throws<InvalidOperationException>(() => conversation.Observe(3, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => conversation.Observe(double.NaN, false, false));
            Assert.AreEqual(count, recorder.Snapshot().Count); Assert.AreEqual(5d, conversation.SecondsSinceOfficerFinished);
        }

        [Test] public void ConstructionRequiresAnActiveSessionAndValidAuthoredConfiguration()
        {
            Assert.Throws<ArgumentNullException>(() => new OfflineCharacterConversation(null, ScenarioCategory.KAI, recorder, clock));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OfflineCharacterConversation(profile, (ScenarioCategory)999, recorder, clock));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OfflineCharacterConversation(profile, ScenarioCategory.KAI, recorder, clock, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OfflineCharacterConversation(profile, ScenarioCategory.KAI, recorder, clock, double.PositiveInfinity));
            recorder.EndSession(SessionEndReason.Completed);
            Assert.Throws<InvalidOperationException>(() => new OfflineCharacterConversation(profile, ScenarioCategory.KAI, recorder, clock));
        }

        [Test] public void MetadataPayloadsRejectInvalidIdentifiersNumbersAndEnums()
        {
            Assert.Throws<ArgumentException>(() => new OfficerTextSubmittedPayload(Guid.Empty, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OfficerTextSubmittedPayload(Guid.NewGuid(), 513));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OfficerProximityPayload(double.NaN, true, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterConditionsPayload(Guid.NewGuid(), (ScenarioCategory)999, 3, false, false, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterConditionsPayload(Guid.NewGuid(), ScenarioCategory.KAI, 3, false, false, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterTurnCancelledPayload(Guid.NewGuid(), (ConversationCancelReason)999));
        }

        [Test] public void ResponseEvidenceRejectsEmptyDuplicateAndUnboundedReferences()
        {
            var id = Guid.NewGuid();
            Assert.Throws<ArgumentException>(() => new CharacterResponsePayload(id, ResponseIntentionType.Wait, "rule", 12, new[] { Guid.Empty, id }));
            Assert.Throws<ArgumentException>(() => new CharacterResponsePayload(id, ResponseIntentionType.Wait, "rule", 12, new[] { id, id }));
            Assert.Throws<ArgumentException>(() => new CharacterResponsePayload(id, ResponseIntentionType.Wait, "rule", 12, new Guid[9]));
            Assert.Throws<ArgumentException>(() => new CharacterResponsePayload(id, ResponseIntentionType.Speak, "rule", 12, new[] { id, Guid.NewGuid() }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterResponsePayload(id, (ResponseIntentionType)999, "rule", 0, new[] { id, Guid.NewGuid() }));
        }

        [Test] public void EndingSessionAlonePreventsLateRepliesWithoutNewEvents()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            recorder.EndSession(SessionEndReason.Completed); int count = recorder.Snapshot().Count;
            clock.Seconds = 20; conversation.Observe(3, false, false); conversation.Cancel();
            Assert.IsFalse(conversation.HasPendingTurn); Assert.IsNull(conversation.CurrentResponse);
            Assert.AreEqual(count, recorder.Snapshot().Count);
        }

        [Test] public void ZeroWaitProfileCanReplyImmediatelyButDoesNotRepeatPerFrame()
        {
            var immediate = new CharacterProfile("another-person", 0, 1, "Hello.", "Quieter please.", "Space please.");
            var other = new OfflineCharacterConversation(immediate, ScenarioCategory.UAI, recorder, clock);
            other.Observe(3, false, false); other.Submit("Hello");
            Assert.AreEqual(ResponseIntentionType.Speak, other.CurrentResponse.Type);
            long revision = other.ResponseRevision;
            clock.Seconds = 10; other.Observe(3, true, true);
            Assert.AreEqual(revision, other.ResponseRevision);
            Assert.AreEqual(1, recorder.Snapshot().Count(e => e.Type == SimulationEventType.CharacterResponseProduced));
        }

        [Test] public void CompletedReplyClearsWhenOfficerLeavesRange()
        {
            conversation.Observe(3, false, false); conversation.Submit("Hello");
            clock.Seconds = 12; conversation.Observe(3, false, false);
            Assert.AreEqual(ResponseIntentionType.Speak, conversation.CurrentResponse.Type);
            long revision = conversation.ResponseRevision;
            conversation.Observe(5, false, false);
            Assert.IsNull(conversation.CurrentResponse); Assert.Greater(conversation.ResponseRevision, revision);
            Assert.AreEqual(0, recorder.Snapshot().Count(e => e.Type == SimulationEventType.CharacterTurnCancelled));
        }
    }
}
