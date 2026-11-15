using System;
using NUnit.Framework;
using SimSafe.Core;

namespace SimSafe.Tests
{
    [TestFixture]
    public sealed class ScenarioEventMonitorTests
    {
        sealed class Clock : ISimulationClock { public double Seconds { get; set; } }
        ScenarioSession session;
        ScenarioEventMonitor monitor;
        Clock clock;

        [SetUp] public void SetUp()
        {
            session = new ScenarioSession(ScenarioSource.Load());
            clock = new Clock(); monitor = new ScenarioEventMonitor(new SimulationEventRecorder(clock));
        }

        [Test] public void UnstartedScenarioCannotBeMonitored()
        {
            Assert.Throws<InvalidOperationException>(() => monitor.Start(session));
            Assert.AreEqual(0, monitor.Snapshot().Count);
        }

        [Test] public void SuccessfulSirenInteractionIsObservedWithoutChangingDecisions()
        {
            session.Start(); monitor.Start(session);
            var rec = session.Interact("car_siren");
            Assert.IsNotNull(rec);
            int decisions = session.Decisions.Count;
            clock.Seconds = 3; monitor.ObserveInteraction(rec);
            Assert.AreEqual(decisions, session.Decisions.Count);
            Assert.AreEqual(2, monitor.Snapshot().Count);
            Assert.AreEqual(3d, monitor.Snapshot()[1].ElapsedSeconds);
            Assert.AreEqual("car_siren", ((EnvironmentInteractionPayload)monitor.Snapshot()[1].Payload).InteractionId);
        }

        [Test] public void NullForgedRepeatedAndOtherInteractionRecordsAreIgnored()
        {
            session.Start(); monitor.Start(session);
            monitor.ObserveInteraction(null);
            monitor.ObserveInteraction(new DecisionRecord { OptionId = "car_siren", IsEnvironmentInteraction = true });
            Assert.AreEqual(1, monitor.Snapshot().Count);
            var rec = session.Interact("car_siren");
            // The expected flag is now true: membership, not just flag validation, must reject this.
            monitor.ObserveInteraction(new DecisionRecord { OptionId = "car_siren", IsEnvironmentInteraction = true });
            Assert.AreEqual(1, monitor.Snapshot().Count);
            monitor.ObserveInteraction(rec); monitor.ObserveInteraction(rec);
            monitor.ObserveInteraction(session.Interact("car_radio"));
            Assert.AreEqual(2, monitor.Snapshot().Count);
        }

        [Test] public void EndIsIdempotentAndDetachedScenarioCannotAddEvents()
        {
            session.Start(); monitor.Start(session);
            monitor.End(SessionEndReason.ReturnedToMenu);
            monitor.End(SessionEndReason.ApplicationStopped);
            monitor.ObserveInteraction(session.Interact("car_siren"));
            Assert.AreEqual(2, monitor.Snapshot().Count);
            Assert.AreEqual(SessionEndReason.ReturnedToMenu, ((SessionEndedPayload)monitor.Snapshot()[1].Payload).Reason);
        }

        [Test] public void RestartIsIsolatedFromOldScenario()
        {
            session.Start(); monitor.Start(session); var oldId = monitor.Snapshot()[0].SessionId;
            monitor.End(SessionEndReason.Restarted);
            var next = new ScenarioSession(ScenarioSource.Load()); next.Start(); monitor.Start(next);
            monitor.ObserveInteraction(session.Interact("car_siren"));
            Assert.AreEqual(1, monitor.Snapshot().Count);
            Assert.IsTrue(oldId != monitor.Snapshot()[0].SessionId);
        }

        [Test] public void BlockedSirenInteractionDoesNotProduceAnEvent()
        {
            session.Start(); monitor.Start(session); session.Choose("arrive_quiet");
            Assert.IsNull(session.Interact("car_siren")); monitor.ObserveInteraction(null);
            Assert.AreEqual(1, monitor.Snapshot().Count);
        }

        [Test] public void OutcomeAutomaticallyClosesRecorderAndPreservesDecisions()
        {
            // Minimal synthetic fixture isolates lifecycle observation from the production content.
            var def = new ScenarioDefinition {
                id = "fixture", startNodeId = "start",
                nodes = new System.Collections.Generic.List<ScenarioNode> {
                    new ScenarioNode { id = "start", kind = "decision", options = new System.Collections.Generic.List<DecisionOption> {
                        new DecisionOption { id = "finish", next = "done" }
                    } },
                    new ScenarioNode { id = "done", kind = "outcome" }
                }
            };
            var run = new ScenarioSession(def); run.Start(); monitor.Start(run);
            run.Choose("finish");
            Assert.IsTrue(run.IsFinished); Assert.AreEqual(1, run.Decisions.Count);
            Assert.AreEqual(SessionEndReason.Completed, ((SessionEndedPayload)monitor.Snapshot()[1].Payload).Reason);
            monitor.End(SessionEndReason.ReturnedToMenu);
            Assert.AreEqual(2, monitor.Snapshot().Count);
        }
    }
}
