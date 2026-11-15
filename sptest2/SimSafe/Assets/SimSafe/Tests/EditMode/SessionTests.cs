using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SimSafe.Core;

namespace SimSafe.Tests
{
    /// <summary>
    /// Tests for scenario branching, state handling and decision evaluation.
    /// They run against the real scenario JSON, so editing the data can break them on purpose:
    /// that is the point of having them.
    /// </summary>
    [TestFixture]
    public class SessionTests
    {
        ScenarioDefinition scenario;

        [SetUp]
        public void SetUp() { scenario = ScenarioSource.Load(); }

        // ---- helpers ------------------------------------------------------

        ScenarioSession Play(params string[] steps)
        {
            var s = new ScenarioSession(scenario);
            s.Start();
            foreach (var step in steps)
            {
                if (step.StartsWith("@")) Assert.IsNotNull(s.Interact(step.Substring(1)), "interaction unavailable: " + step);
                else s.Choose(step);
            }
            return s;
        }

        // ---- data integrity ----------------------------------------------

        [Test]
        public void ScenarioDataIsStructurallyValid()
        {
            var errors = ScenarioValidator.Validate(scenario);
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors.ToArray()));
        }

        [Test]
        public void EveryCriterionIsUsedByAtLeastOneOptionOrInteraction()
        {
            var used = new HashSet<string>();
            foreach (var n in scenario.nodes) foreach (var o in n.options) used.Add(o.criterionId ?? "");
            foreach (var i in scenario.interactions) used.Add(i.criterionId ?? "");
            foreach (var c in scenario.criteria) Assert.IsTrue(used.Contains(c.id), "Criterion never referenced: " + c.id);
        }

        [Test]
        public void EverySlowPrincipleHasAnAlignedOption()
        {
            foreach (var letter in new[] { "S", "L", "O", "W" })
            {
                bool found = scenario.nodes.SelectMany(n => n.options).Any(o => o.principle == letter && o.alignment == "aligned");
                Assert.IsTrue(found, "No aligned option for principle " + letter);
            }
        }

        [Test]
        public void WaitOptionsUseTheTenToTwentySecondWindow()
        {
            var waits = scenario.nodes.SelectMany(n => n.options).Where(o => o.waitSeconds > 0).ToList();
            Assert.Greater(waits.Count, 0);
            foreach (var o in waits)
            {
                Assert.GreaterOrEqual(o.waitSeconds, 10f, o.id);
                Assert.LessOrEqual(o.waitSeconds, 20f, o.id);
            }
        }

        // ---- branching ----------------------------------------------------

        [Test]
        public void CalmApproachAfterLoweringNoiseGoesToQuietContact()
        {
            var s = Play("arrive_quiet", "look_observe", "path_calm");
            Assert.AreEqual("calm_quiet", s.CurrentNode.id);
        }

        [Test]
        public void ApproachWhileSceneIsStillLoudGoesToNoisyContact()
        {
            // Siren left on: sensory starts at 70, rises to 75, so recovering after a rushed start still lands in a loud scene.
            var s = Play("arrive_fast", "rush_stop");
            Assert.GreaterOrEqual(s.Sensory, 50);
            Assert.AreEqual("calm_noisy", s.CurrentNode.id);
        }

        [Test]
        public void RequestingBackupInsertsTheOneSpeakerDecision()
        {
            var s = Play("arrive_backup", "look_observe", "path_calm");
            Assert.AreEqual("second_officer", s.CurrentNode.id);
            Assert.IsTrue(s.HasFlag("backupArrived"));
            s.Choose("so_one_voice");
            Assert.IsTrue(s.CurrentNode.id == "calm_noisy" || s.CurrentNode.id == "calm_quiet");
        }

        [Test]
        public void TwoOfficersSpeakingAtOnceEscalates()
        {
            var s = Play("arrive_backup", "look_observe", "path_calm", "so_both");
            Assert.AreEqual("commands_escalate", s.CurrentNode.id);
        }

        [Test]
        public void AskingCaregiverAboutSupportsChangesTheResponseAfterWaiting()
        {
            var informed = Play("arrive_quiet", "look_observe", "path_caregiver", "cg_supports", "cg_return", "path_calm", "cq_wait");
            Assert.AreEqual("sam_supported", informed.CurrentNode.id);

            var uninformed = Play("arrive_quiet", "look_observe", "path_calm", "cq_wait");
            Assert.AreEqual("sam_hesitant", uninformed.CurrentNode.id);
        }

        [Test]
        public void StatThresholdBranchSendsLateDeescalationToDifficultOutcome()
        {
            var s = Play("arrive_fast", "rush_continue", "ce_deescalate");
            Assert.GreaterOrEqual(s.Distress, 75);
            Assert.AreEqual("outcome_difficult", s.CurrentNode.id);
        }

        [Test]
        public void EarlyDeescalationRecoversButStillFacesTheNoiseItCreated()
        {
            // Loud commands add sensory load, so recovery lands in the noisy-scene branch rather than the quiet one.
            var s = Play("arrive_quiet", "look_observe", "path_commands", "ce_deescalate");
            Assert.IsFalse(s.IsFinished);
            Assert.AreEqual("calm_noisy", s.CurrentNode.id);
            s.Choose("cn_lower");
            Assert.AreEqual("calm_quiet", s.CurrentNode.id);
        }

        // ---- outcomes: good choices do not guarantee a good outcome ---------

        [Test]
        public void IdealPathReachesPositiveOutcome()
        {
            var s = Play("arrive_quiet", "look_observe", "path_caregiver", "cg_supports", "cg_safety", "cg_return", "path_calm", "cq_wait", "ss_offer");
            Assert.IsTrue(s.IsFinished);
            Assert.AreEqual("outcome_positive", s.CurrentNode.id);
        }

        [Test]
        public void AllAlignedChoicesCanStillEndUnresolved()
        {
            var s = Play("arrive_quiet", "look_observe", "path_calm", "cq_wait", "sh_wait");
            Assert.AreEqual("outcome_mixed", s.CurrentNode.id);
            Assert.IsFalse(s.Decisions.Any(d => d.Alignment == Alignment.Misaligned), "Path should contain no misaligned decisions");

            var debrief = DebriefBuilder.Build(s);
            Assert.AreEqual(OutcomeTone.Mixed, debrief.Tone);
            Assert.IsTrue(debrief.Observations.Any(o => o.Contains("did not fully resolve")));
        }

        [Test]
        public void RushedPathReachesDifficultOutcome()
        {
            var s = Play("arrive_fast", "rush_continue", "ce_louder");
            Assert.AreEqual("outcome_difficult", s.CurrentNode.id);
        }

        [Test]
        public void AllThreeOutcomesAreReachableAndNoPathSoftLocks()
        {
            var reached = new HashSet<string>();
            var seen = new HashSet<string>();
            var queue = new Queue<List<string>>();
            queue.Enqueue(new List<string>());
            int explored = 0;

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                var s = Play(path.ToArray());
                string key = StateKey(s);
                if (!seen.Add(key)) continue;
                explored++;

                Assert.IsTrue(s.Distress >= 0 && s.Distress <= 100 && s.Trust >= 0 && s.Trust <= 100 && s.Sensory >= 0 && s.Sensory <= 100,
                    "Stat out of range after: " + string.Join(",", path.ToArray()));

                if (s.IsFinished) { reached.Add(s.CurrentNode.id); continue; }

                var options = s.GetAvailableOptions();
                Assert.Greater(options.Count, 0, "Soft lock at node '" + s.CurrentNode.id + "' after: " + string.Join(",", path.ToArray()));
                foreach (var o in options) queue.Enqueue(new List<string>(path) { o.id });
                foreach (var i in scenario.interactions) if (s.CanInteract(i.id)) queue.Enqueue(new List<string>(path) { "@" + i.id });
                Assert.Less(explored, 200000, "State space unexpectedly large - possible loop");
            }
            Assert.IsTrue(reached.Contains("outcome_positive"), "positive outcome unreachable");
            Assert.IsTrue(reached.Contains("outcome_mixed"), "mixed outcome unreachable");
            Assert.IsTrue(reached.Contains("outcome_difficult"), "difficult outcome unreachable");
        }

        static string StateKey(ScenarioSession s)
        {
            var chosen = s.Decisions.Select(d => d.OptionId).OrderBy(x => x, StringComparer.Ordinal);
            return s.CurrentNode.id + "|" + s.Distress + "|" + s.Trust + "|" + s.Sensory
                + "|" + string.Join(",", s.Flags.OrderBy(x => x, StringComparer.Ordinal).ToArray())
                + "|" + string.Join(",", chosen.ToArray());
        }

        // ---- state handling -----------------------------------------------

        [Test]
        public void StatsAreClampedToZeroAndOneHundred()
        {
            var s = Play("arrive_fast", "rush_continue", "ce_louder"); // 55+10+20+20 would exceed 100
            Assert.AreEqual(100, s.Distress);
            var calm = Play("arrive_quiet", "@car_radio", "@neighbour_mower", "look_observe", "path_noise");
            Assert.AreEqual(0, calm.Sensory);
        }

        [Test]
        public void OneShotOptionsDisappearAfterUse()
        {
            var s = Play("arrive_quiet", "look_observe", "path_caregiver");
            s.Choose("cg_supports");
            Assert.IsFalse(s.GetAvailableOptions().Any(o => o.id == "cg_supports"));
            Assert.IsTrue(s.GetAvailableOptions().Any(o => o.id == "cg_return"));
        }

        [Test]
        public void CaregiverOptionIsNotOfferedAgainAfterUse()
        {
            var s = Play("arrive_quiet", "look_observe", "path_caregiver", "cg_return");
            Assert.AreEqual("choose_path", s.CurrentNode.id);
            Assert.IsFalse(s.GetAvailableOptions().Any(o => o.id == "path_caregiver"));
        }

        [Test]
        public void UnavailableOptionThrows()
        {
            var s = new ScenarioSession(scenario);
            s.Start();
            Assert.Throws<InvalidOperationException>(() => s.Choose("ss_offer"));
            Assert.Throws<InvalidOperationException>(() => s.Choose("does_not_exist"));
        }

        [Test]
        public void EnvironmentInteractionsChangeStateOnceAndAreRecorded()
        {
            var s = new ScenarioSession(scenario);
            s.Start();
            Assert.IsTrue(s.CanInteract("car_siren"));
            var rec = s.Interact("car_siren");
            Assert.IsNotNull(rec);
            Assert.AreEqual(50, s.Sensory);
            Assert.IsTrue(s.HasFlag("sirenOff"));
            Assert.IsTrue(rec.IsEnvironmentInteraction);
            Assert.IsFalse(s.CanInteract("car_siren"));
            Assert.IsNull(s.Interact("car_siren"));
            Assert.AreEqual(1, s.Decisions.Count);
        }

        [Test]
        public void SirenInteractionIsBlockedOnceArrivalChoiceSwitchedItOff()
        {
            var s = Play("arrive_quiet");
            Assert.IsFalse(s.CanInteract("car_siren"));
        }

        [Test]
        public void RestartResetsEverything()
        {
            var s = Play("arrive_fast", "rush_continue", "ce_louder");
            Assert.IsTrue(s.IsFinished);
            s.Start();
            Assert.AreEqual("arrive", s.CurrentNode.id);
            Assert.AreEqual(55, s.Distress);
            Assert.AreEqual(70, s.Sensory);
            Assert.AreEqual(0, s.Decisions.Count);
            Assert.IsFalse(s.HasFlag("sirenOff"));
        }

        // ---- decision evaluation -------------------------------------------

        [Test]
        public void DebriefRecordsDecisionsAlternativesAndPrinciples()
        {
            var s = Play("arrive_fast", "rush_continue", "ce_louder");
            var d = DebriefBuilder.Build(s);
            Assert.AreEqual(3, d.Decisions.Count);
            Assert.AreEqual(4, d.Principles.Count);
            Assert.Greater(d.Decisions[0].Alternatives.Count, 0, "alternatives should be recorded for explanation");

            var slow = d.Principles.First(p => p.Principle == Principle.Slow);
            Assert.AreEqual(2, slow.Misaligned);
            Assert.AreEqual(0, slow.Aligned);
        }

        [Test]
        public void MissedCriterionProducesItsConfiguredSuggestion()
        {
            var s = Play("arrive_fast", "rush_continue", "ce_louder");
            var d = DebriefBuilder.Build(s);
            var slow = d.Criteria.First(c => c.Criterion.id == "c-slow");
            Assert.AreEqual(CriterionStatus.Missed, slow.Status);
            Assert.IsTrue(d.Suggestions.Contains(slow.Criterion.suggestion));
        }

        [Test]
        public void CriterionStatusRulesCoverDemonstratedPartialAndNotEncountered()
        {
            var ideal = DebriefBuilder.Build(Play("arrive_quiet", "look_observe", "path_caregiver", "cg_supports", "cg_return", "path_calm", "cq_wait", "ss_offer"));
            Assert.AreEqual(CriterionStatus.Demonstrated, ideal.Criteria.First(c => c.Criterion.id == "c-wait").Status);
            Assert.AreEqual(CriterionStatus.NotEncountered, ideal.Criteria.First(c => c.Criterion.id == "c-onespeaker").Status);

            // c-wait: aligned wait, then misaligned repeat -> Partial
            var partial = DebriefBuilder.Build(Play("arrive_quiet", "look_observe", "path_calm", "cq_wait", "sh_repeat", "ov_back"));
            Assert.AreEqual(CriterionStatus.Partial, partial.Criteria.First(c => c.Criterion.id == "c-wait").Status);
        }

        [Test]
        public void NeutralDecisionsAreRecordedButNotScored()
        {
            var s = Play("arrive_quiet", "look_observe", "path_caregiver", "cg_return");
            var d = DebriefBuilder.Build(s);
            Assert.IsTrue(d.Decisions.Any(x => x.Alignment == Alignment.Neutral));
            Assert.AreEqual(0, d.Principles.Sum(p => p.Misaligned));
        }

        [Test]
        public void DebriefCarriesTheIllustrativeDisclaimer()
        {
            var d = DebriefBuilder.Build(Play("arrive_quiet"));
            Assert.IsFalse(d.Completed);
            StringAssert.Contains("illustrative", d.Disclaimer.ToLowerInvariant());
            StringAssert.Contains("not yet reviewed", d.Status.ToLowerInvariant());
        }

        [Test]
        public void ValidatorCatchesBrokenData()
        {
            var broken = ScenarioSource.Load();
            broken.nodes[0].options[0].next = "nowhere";
            broken.nodes.Add(new ScenarioNode { id = "orphan", kind = "outcome", outcomeTitle = "x" });
            var errors = ScenarioValidator.Validate(broken);
            Assert.IsTrue(errors.Any(e => e.Contains("nowhere")));
            Assert.IsTrue(errors.Any(e => e.Contains("orphan")));
        }
    }
}
