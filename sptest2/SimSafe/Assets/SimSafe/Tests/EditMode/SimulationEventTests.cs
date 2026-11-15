using System;
using System.Collections.Generic;
using NUnit.Framework;
using SimSafe.Core;

namespace SimSafe.Tests
{
    [TestFixture]
    public sealed class SimulationEventTests
    {
        sealed class Clock : ISimulationClock { public double Seconds { get; set; } }
        Clock clock;
        SimulationEventRecorder recorder;

        [SetUp] public void SetUp()
        {
            clock = new Clock { Seconds = 100 };
            recorder = new SimulationEventRecorder(clock);
        }

        SimulationEvent Record() => recorder.Record("officer", "patrol", new EnvironmentInteractionPayload("car_siren", false));

        [Test] public void ProcessClockSupportsStartAndTeardownWithoutUnityTime()
        {
            var processRecorder = new SimulationEventRecorder(new StopwatchSimulationClock());
            processRecorder.StartSession("welfare", "officer");
            processRecorder.EndSession(SessionEndReason.ApplicationStopped);
            Assert.AreEqual(2, processRecorder.Snapshot().Count);
            Assert.GreaterOrEqual(processRecorder.Snapshot()[1].ElapsedSeconds, 0d);
            Assert.IsFalse(processRecorder.IsActive);
        }

        [Test] public void InvalidRestartPreservesCompletedHistory()
        {
            recorder.StartSession("welfare", "officer"); recorder.EndSession(SessionEndReason.Completed);
            var oldId = recorder.SessionId;
            Assert.Throws<ArgumentException>(() => recorder.StartSession("bad id", "officer"));
            Assert.AreEqual(oldId, recorder.SessionId); Assert.AreEqual(2, recorder.Snapshot().Count);
        }

        [Test] public void StartCreatesUniqueSessionAndZeroTimestamp()
        {
            var id = recorder.StartSession("welfare", "officer");
            var first = recorder.Snapshot()[0];
            Assert.IsTrue(id != Guid.Empty);
            Assert.AreEqual(id, first.SessionId);
            Assert.AreEqual("welfare", first.ScenarioId);
            Assert.AreEqual("officer", first.ActorId);
            Assert.AreEqual("welfare", first.TargetId);
            Assert.AreEqual(0d, first.ElapsedSeconds);
            Assert.AreEqual(SimulationEventType.SessionStarted, first.Type);
        }

        [Test] public void RecordsElapsedTimeAndTypedControlPayload()
        {
            recorder.StartSession("welfare", "officer"); clock.Seconds = 102.25;
            var value = Record();
            Assert.AreEqual(2.25d, value.ElapsedSeconds);
            Assert.AreEqual(SimulationEventType.EnvironmentInteractionSucceeded, value.Type);
            Assert.AreEqual("patrol", value.TargetId);
            Assert.AreEqual("car_siren", ((EnvironmentInteractionPayload)value.Payload).InteractionId);
            Assert.IsFalse(((EnvironmentInteractionPayload)value.Payload).Enabled);
            Assert.IsTrue(value.EventId != recorder.Snapshot()[0].EventId);
        }

        [Test] public void EqualClockReadingsAreAllowedAndOrderedBySnapshot()
        {
            recorder.StartSession("welfare", "officer");
            var a = Record(); var b = Record();
            Assert.AreEqual(a.ElapsedSeconds, b.ElapsedSeconds);
            Assert.IsTrue(a.EventId != b.EventId);
            Assert.AreEqual(a, recorder.Snapshot()[1]);
            Assert.AreEqual(b, recorder.Snapshot()[2]);
        }

        [Test] public void ClockRegressionIsRejectedWithoutAppending()
        {
            recorder.StartSession("welfare", "officer"); clock.Seconds = 105; Record();
            clock.Seconds = 104;
            Assert.Throws<InvalidOperationException>(() => Record());
            Assert.AreEqual(2, recorder.Snapshot().Count);
            clock.Seconds = 106; Assert.AreEqual(6d, Record().ElapsedSeconds);
        }

        [Test] public void InvalidClockValuesAreRejectedWithoutAppending()
        {
            recorder.StartSession("welfare", "officer");
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
            {
                clock.Seconds = value;
                Assert.Throws<InvalidOperationException>(() => Record());
                Assert.AreEqual(1, recorder.Snapshot().Count);
            }
        }

        [Test] public void InvalidStartingClockDoesNotOpenSession()
        {
            clock.Seconds = double.NaN;
            Assert.Throws<InvalidOperationException>(() => recorder.StartSession("welfare", "officer"));
            Assert.IsFalse(recorder.IsActive); Assert.AreEqual(0, recorder.Snapshot().Count);
        }

        [Test] public void EndRecordsReasonAndClosesWrites()
        {
            recorder.StartSession("welfare", "officer"); clock.Seconds = 108;
            recorder.EndSession(SessionEndReason.Completed);
            var last = recorder.Snapshot()[1];
            Assert.AreEqual(SimulationEventType.SessionEnded, last.Type);
            Assert.AreEqual(8d, last.ElapsedSeconds);
            Assert.AreEqual(SessionEndReason.Completed, ((SessionEndedPayload)last.Payload).Reason);
            Assert.IsFalse(recorder.IsActive);
            Assert.Throws<InvalidOperationException>(() => Record());
            Assert.Throws<InvalidOperationException>(() => recorder.EndSession(SessionEndReason.Completed));
        }

        [Test] public void NewSessionsHaveIsolatedHistoryAndIdentifiers()
        {
            var firstId = recorder.StartSession("first", "officer"); Record();
            recorder.EndSession(SessionEndReason.Restarted);
            var old = recorder.Snapshot();
            clock.Seconds = 120;
            var nextId = recorder.StartSession("second", "officer");
            Assert.IsTrue(firstId != nextId);
            Assert.AreEqual(1, recorder.Snapshot().Count);
            Assert.AreEqual(0d, recorder.Snapshot()[0].ElapsedSeconds);
            Assert.AreEqual(3, old.Count);
            foreach (var value in old) Assert.AreEqual(firstId, value.SessionId);
            Assert.AreEqual("second", recorder.Snapshot()[0].ScenarioId);
        }

        [Test] public void SnapshotIsReadOnlyAndDoesNotTrackFutureWrites()
        {
            recorder.StartSession("welfare", "officer"); var snapshot = recorder.Snapshot();
            Record(); Assert.AreEqual(1, snapshot.Count);
            var list = (IList<SimulationEvent>)snapshot;
            Assert.Throws<NotSupportedException>(() => list.Clear());
            Assert.Throws<NotSupportedException>(() => list[0] = recorder.Snapshot()[1]);
            Assert.AreEqual(2, recorder.Snapshot().Count);
        }

        [Test] public void RecordRequiresActiveSession()
        {
            Assert.Throws<InvalidOperationException>(() => Record());
            Assert.Throws<InvalidOperationException>(() => recorder.EndSession(SessionEndReason.Completed));
        }

        [Test] public void OverlappingStartDoesNotDestroyHistory()
        {
            var id = recorder.StartSession("first", "officer"); Record();
            Assert.Throws<InvalidOperationException>(() => recorder.StartSession("second", "officer"));
            Assert.AreEqual(id, recorder.SessionId); Assert.AreEqual(2, recorder.Snapshot().Count);
        }

        [Test] public void InvalidIdentifiersDoNotMutateRecorder()
        {
            Assert.Throws<ArgumentException>(() => recorder.StartSession(" ", "officer"));
            Assert.Throws<ArgumentException>(() => recorder.StartSession("welfare", ""));
            recorder.StartSession("welfare", "officer");
            Assert.Throws<ArgumentException>(() => recorder.Record("", "patrol", new EnvironmentInteractionPayload("car_siren", false)));
            Assert.Throws<ArgumentException>(() => recorder.Record("officer", "bad\nidentifier", new EnvironmentInteractionPayload("car_siren", false)));
            Assert.Throws<ArgumentException>(() => new EnvironmentInteractionPayload(new string('x', 129), false));
            Assert.AreEqual(1, recorder.Snapshot().Count);
        }

        [Test] public void NullClockAndPayloadAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new SimulationEventRecorder(null));
            recorder.StartSession("welfare", "officer");
            Assert.Throws<ArgumentNullException>(() => recorder.Record("officer", "patrol", null));
            Assert.AreEqual(1, recorder.Snapshot().Count);
        }

        [Test] public void BoundariesCannotBeInsertedThroughRecord()
        {
            recorder.StartSession("welfare", "officer");
            Assert.Throws<ArgumentException>(() => recorder.Record("officer", "welfare", new SessionStartedPayload()));
            Assert.Throws<ArgumentException>(() => recorder.Record("officer", "welfare", new SessionEndedPayload(SessionEndReason.Completed)));
            Assert.AreEqual(1, recorder.Snapshot().Count);
        }

        [Test] public void UndefinedEndReasonDoesNotCloseSession()
        {
            recorder.StartSession("welfare", "officer");
            Assert.Throws<ArgumentOutOfRangeException>(() => recorder.EndSession((SessionEndReason)999));
            Assert.IsTrue(recorder.IsActive); Assert.AreEqual(1, recorder.Snapshot().Count);
        }

        [Test] public void EventRejectsMismatchedOrUndefinedType()
        {
            var payload = new EnvironmentInteractionPayload("car_siren", false);
            Assert.Throws<ArgumentException>(() => new SimulationEvent(Guid.NewGuid(), Guid.NewGuid(), "welfare", "officer", "patrol", 0, SimulationEventType.SessionStarted, payload));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationEvent(Guid.NewGuid(), Guid.NewGuid(), "welfare", "officer", "patrol", 0, (SimulationEventType)999, payload));
        }

        [Test] public void EventRejectsEmptyGuidsAndInvalidTime()
        {
            var payload = new EnvironmentInteractionPayload("car_siren", false);
            Assert.Throws<ArgumentException>(() => new SimulationEvent(Guid.Empty, Guid.NewGuid(), "welfare", "officer", "patrol", 0, payload.Type, payload));
            Assert.Throws<ArgumentException>(() => new SimulationEvent(Guid.NewGuid(), Guid.Empty, "welfare", "officer", "patrol", 0, payload.Type, payload));
            foreach (double time in new[] { -1d, double.NaN, double.PositiveInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationEvent(Guid.NewGuid(), Guid.NewGuid(), "welfare", "officer", "patrol", time, payload.Type, payload));
        }

        [Test] public void FailedEndWithBadClockLeavesSessionOpen()
        {
            recorder.StartSession("welfare", "officer"); clock.Seconds = 99;
            Assert.Throws<InvalidOperationException>(() => recorder.EndSession(SessionEndReason.Completed));
            Assert.IsTrue(recorder.IsActive); Assert.AreEqual(1, recorder.Snapshot().Count);
        }
    }
}
