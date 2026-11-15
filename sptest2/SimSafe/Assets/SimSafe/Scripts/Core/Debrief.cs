using System;
using System.Collections.Generic;
using System.Linq;

namespace SimSafe.Core
{
    public enum CriterionStatus { NotEncountered, Demonstrated, Partial, Missed }

    public sealed class PrincipleSummary
    {
        public Principle Principle;
        public int Aligned, Mixed, Misaligned;
        public int Total { get { return Aligned + Mixed + Misaligned; } }
        public string Headline;
    }

    public sealed class CriterionResult
    {
        public Criterion Criterion;
        public CriterionStatus Status;
        public readonly List<DecisionRecord> Decisions = new List<DecisionRecord>();
    }

    public sealed class Debrief
    {
        public string ScenarioTitle, Status, Disclaimer, NonGuaranteeNote;
        public string OutcomeTitle, OutcomeSummary;
        public OutcomeTone Tone;
        public bool Completed;
        public IReadOnlyList<DecisionRecord> Decisions;
        public List<PrincipleSummary> Principles = new List<PrincipleSummary>();
        public List<CriterionResult> Criteria = new List<CriterionResult>();
        public List<string> Suggestions = new List<string>();
        public List<string> Observations = new List<string>();
    }

    /// <summary>
    /// Turns a finished (or in-progress) session into a debrief. Criteria come from the
    /// scenario data, so reviewers can change what is assessed without touching code.
    /// This reports alignment with the illustrative SLOW criteria; it does not score competence.
    /// </summary>
    public static class DebriefBuilder
    {
        static readonly Principle[] Order = { Principle.Slow, Principle.Lower, Principle.Observe, Principle.Wait };

        public static Debrief Build(ScenarioSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            var sc = session.Scenario;
            var node = session.CurrentNode;
            var d = new Debrief
            {
                ScenarioTitle = sc.title, Status = sc.status, Disclaimer = sc.disclaimer, NonGuaranteeNote = sc.nonGuaranteeNote,
                Completed = session.IsFinished,
                Decisions = session.Decisions,
                OutcomeTitle = session.IsFinished ? node.outcomeTitle : "Scenario not completed",
                OutcomeSummary = session.IsFinished ? node.outcomeSummary : string.Empty,
                Tone = session.IsFinished ? ScenarioParse.ToTone(node.outcomeTone) : OutcomeTone.None
            };

            var scored = session.Decisions.Where(r => r.Alignment != Alignment.Neutral && r.Principle != Principle.None).ToList();

            foreach (var p in Order)
            {
                var ps = new PrincipleSummary { Principle = p };
                foreach (var r in scored.Where(r => r.Principle == p))
                {
                    if (r.Alignment == Alignment.Aligned) ps.Aligned++;
                    else if (r.Alignment == Alignment.Mixed) ps.Mixed++;
                    else ps.Misaligned++;
                }
                ps.Headline = ps.Total == 0
                    ? "Not encountered in this playthrough"
                    : ps.Aligned + " of " + ps.Total + " decisions aligned" + (ps.Mixed > 0 ? ", " + ps.Mixed + " mixed" : "");
                d.Principles.Add(ps);
            }

            foreach (var c in sc.criteria)
            {
                var res = new CriterionResult { Criterion = c };
                res.Decisions.AddRange(scored.Where(r => r.CriterionId == c.id));
                int a = res.Decisions.Count(r => r.Alignment == Alignment.Aligned);
                int x = res.Decisions.Count(r => r.Alignment == Alignment.Mixed);
                int m = res.Decisions.Count(r => r.Alignment == Alignment.Misaligned);
                if (res.Decisions.Count == 0) res.Status = CriterionStatus.NotEncountered;
                else if (x == 0 && m == 0) res.Status = CriterionStatus.Demonstrated;
                else if (a == 0 && x == 0) res.Status = CriterionStatus.Missed;
                else res.Status = CriterionStatus.Partial;
                d.Criteria.Add(res);

                if ((res.Status == CriterionStatus.Partial || res.Status == CriterionStatus.Missed)
                    && !string.IsNullOrEmpty(c.suggestion) && !d.Suggestions.Contains(c.suggestion))
                    d.Suggestions.Add(c.suggestion);
            }

            bool anyMisaligned = scored.Any(r => r.Alignment == Alignment.Misaligned);
            if (session.IsFinished)
            {
                if (d.Tone == OutcomeTone.Positive && anyMisaligned)
                    d.Observations.Add("The outcome was calm, but some choices along the way were flagged. Other people may not respond as forgivingly.");
                if (d.Tone != OutcomeTone.Positive && !anyMisaligned && scored.Count > 0)
                    d.Observations.Add("None of your choices were flagged as misaligned, yet the encounter did not fully resolve. Good practice lowers risk but cannot guarantee how any individual will respond.");
            }
            if (d.Suggestions.Count == 0 && scored.Count > 0)
                d.Observations.Add("No improvement suggestions were triggered by this playthrough. Try the other branches to see how different choices change the encounter.");
            return d;
        }
    }
}
