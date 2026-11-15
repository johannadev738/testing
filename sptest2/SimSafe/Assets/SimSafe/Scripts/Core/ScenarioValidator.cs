using System;
using System.Collections.Generic;

namespace SimSafe.Core
{
    /// <summary>
    /// Structural checks on scenario data, so authoring mistakes show up as readable
    /// messages instead of runtime exceptions. Returns an empty list when valid.
    /// </summary>
    public static class ScenarioValidator
    {
        public static List<string> Validate(ScenarioDefinition s)
        {
            var errors = new List<string>();
            if (s == null) { errors.Add("Scenario is null."); return errors; }
            if (string.IsNullOrEmpty(s.id)) errors.Add("Scenario has no id.");
            if (string.IsNullOrEmpty(s.title)) errors.Add("Scenario has no title.");

            var nodeIds = new HashSet<string>();
            var optionIds = new HashSet<string>();
            var criterionIds = new HashSet<string>();

            foreach (var c in s.criteria)
            {
                if (!criterionIds.Add(c.id ?? "")) errors.Add("Duplicate criterion id '" + c.id + "'.");
                Principle p;
                if (!ScenarioParse.TryPrinciple(c.principle, out p)) errors.Add("Criterion '" + c.id + "' has invalid principle '" + c.principle + "'.");
            }

            foreach (var n in s.nodes)
                if (!nodeIds.Add(n.id ?? "")) errors.Add("Duplicate node id '" + n.id + "'.");

            if (!nodeIds.Contains(s.startNodeId ?? "")) errors.Add("startNodeId '" + s.startNodeId + "' does not exist.");

            foreach (var n in s.nodes)
            {
                string where = "Node '" + n.id + "': ";
                if (n.kind != "decision" && n.kind != "outcome") errors.Add(where + "kind must be 'decision' or 'outcome'.");
                if (n.kind == "decision")
                {
                    if (string.IsNullOrEmpty(n.trigger)) { /* defaults to auto */ }
                    else if (n.trigger != "auto" && n.trigger != "interact") errors.Add(where + "trigger must be 'auto' or 'interact'.");
                    if (n.options.Count == 0) errors.Add(where + "decision node has no options.");
                    bool hasAlwaysAvailable = false;
                    foreach (var o in n.options)
                    {
                        if (!o.oneShot && o.requiresFlags.Count == 0) hasAlwaysAvailable = true;
                        ValidateOption(s, o, where, optionIds, nodeIds, criterionIds, errors);
                    }
                    if (n.options.Count > 0 && !hasAlwaysAvailable)
                        errors.Add(where + "every option is one-shot or flag-gated, so the player could be left with no choice.");
                }
                else if (n.kind == "outcome")
                {
                    if (string.IsNullOrEmpty(n.outcomeTitle)) errors.Add(where + "outcome has no outcomeTitle.");
                    OutcomeTone t;
                    if (!ScenarioParse.TryTone(n.outcomeTone, out t)) errors.Add(where + "invalid outcomeTone '" + n.outcomeTone + "'.");
                }
            }

            var interactionIds = new HashSet<string>();
            foreach (var i in s.interactions)
            {
                if (!interactionIds.Add(i.id ?? "")) errors.Add("Duplicate interaction id '" + i.id + "'.");
                if (!string.IsNullOrEmpty(i.criterionId) && !criterionIds.Contains(i.criterionId))
                    errors.Add("Interaction '" + i.id + "' references unknown criterion '" + i.criterionId + "'.");
            }

            // Reachability from the start node.
            if (nodeIds.Contains(s.startNodeId ?? ""))
            {
                var byId = new Dictionary<string, ScenarioNode>();
                foreach (var n in s.nodes) byId[n.id] = n;
                var seen = new HashSet<string>();
                var stack = new Stack<string>();
                stack.Push(s.startNodeId);
                while (stack.Count > 0)
                {
                    var id = stack.Pop();
                    if (!seen.Add(id)) continue;
                    ScenarioNode node;
                    if (!byId.TryGetValue(id, out node)) continue;
                    foreach (var o in node.options)
                    {
                        if (!string.IsNullOrEmpty(o.next)) stack.Push(o.next);
                        foreach (var b in o.branches) if (!string.IsNullOrEmpty(b.next)) stack.Push(b.next);
                    }
                }
                foreach (var n in s.nodes) if (!seen.Contains(n.id)) errors.Add("Node '" + n.id + "' is unreachable from the start node.");
            }
            return errors;
        }

        static void ValidateOption(ScenarioDefinition s, DecisionOption o, string where, HashSet<string> optionIds,
            HashSet<string> nodeIds, HashSet<string> criterionIds, List<string> errors)
        {
            string w = where + "option '" + o.id + "': ";
            if (!optionIds.Add(o.id ?? "")) errors.Add(w + "duplicate option id.");
            Principle p; Alignment a;
            if (!ScenarioParse.TryPrinciple(o.principle, out p)) errors.Add(w + "invalid principle '" + o.principle + "'.");
            if (!ScenarioParse.TryAlignment(o.alignment, out a)) errors.Add(w + "invalid alignment '" + o.alignment + "'.");
            if (!string.IsNullOrEmpty(o.criterionId) && !criterionIds.Contains(o.criterionId))
                errors.Add(w + "unknown criterionId '" + o.criterionId + "'.");
            if (a != Alignment.Neutral && string.IsNullOrEmpty(o.criterionId))
                errors.Add(w + "non-neutral option should reference a criterion so the debrief can explain it.");
            if (string.IsNullOrEmpty(o.next) || !nodeIds.Contains(o.next)) errors.Add(w + "next '" + o.next + "' does not exist.");
            foreach (var b in o.branches)
            {
                if (!nodeIds.Contains(b.next ?? "")) errors.Add(w + "branch target '" + b.next + "' does not exist.");
                foreach (var c in b.stats)
                {
                    string st = (c.stat ?? "").ToLowerInvariant();
                    if (st != "distress" && st != "trust" && st != "sensory") errors.Add(w + "unknown stat '" + c.stat + "'.");
                    switch (c.op) { case "<": case "<=": case ">": case ">=": case "==": case "!=": break; default: errors.Add(w + "unknown operator '" + c.op + "'."); break; }
                }
            }
        }
    }
}
