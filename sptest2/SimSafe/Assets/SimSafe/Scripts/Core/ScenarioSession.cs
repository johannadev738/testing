using System;
using System.Collections.Generic;
using System.Linq;

namespace SimSafe.Core
{
    public sealed class AlternativeRecord
    {
        public string Text;
        public Principle Principle;
        public Alignment Alignment;
        public string Note;
    }

    public sealed class DecisionRecord
    {
        public int Order;
        public string NodeId;
        public string Prompt;
        public string OptionId;
        public string ChosenText;
        public Principle Principle;
        public Alignment Alignment;
        public string CriterionId;
        public string Feedback;
        public bool IsEnvironmentInteraction;
        public float WaitSeconds;
        public int DistressAfter, TrustAfter, SensoryAfter;
        public readonly List<AlternativeRecord> Alternatives = new List<AlternativeRecord>();
    }

    /// <summary>
    /// Runs one playthrough of a ScenarioDefinition. Pure C#: no engine types,
    /// no clocks, no randomness. The same choices always give the same result,
    /// which is what makes branching testable.
    /// </summary>
    public sealed class ScenarioSession
    {
        public const int MinStat = 0, MaxStat = 100;

        readonly Dictionary<string, ScenarioNode> nodes = new Dictionary<string, ScenarioNode>(StringComparer.Ordinal);
        readonly Dictionary<string, InteractionDef> interactions = new Dictionary<string, InteractionDef>(StringComparer.Ordinal);
        readonly HashSet<string> flags = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> usedOptions = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> usedInteractions = new HashSet<string>(StringComparer.Ordinal);
        readonly List<DecisionRecord> decisions = new List<DecisionRecord>();

        public ScenarioDefinition Scenario { get; private set; }
        public ScenarioNode CurrentNode { get; private set; }
        public DecisionOption LastOption { get; private set; }
        public int Distress { get; private set; }
        public int Trust { get; private set; }
        public int Sensory { get; private set; }
        public bool IsStarted { get { return CurrentNode != null; } }
        public bool IsFinished { get { return CurrentNode != null && CurrentNode.kind == "outcome"; } }
        public IReadOnlyList<DecisionRecord> Decisions { get { return decisions; } }
        public IEnumerable<string> Flags { get { return flags; } }

        /// <summary>Raised after any change to node, stats or flags.</summary>
        public event Action Changed;
        public event Action<ScenarioNode> NodeEntered;

        public ScenarioSession(ScenarioDefinition scenario)
        {
            if (scenario == null) throw new ArgumentNullException("scenario");
            Scenario = scenario;
            foreach (var n in scenario.nodes) nodes[n.id] = n;
            foreach (var i in scenario.interactions) interactions[i.id] = i;
        }

        public bool HasFlag(string flag) { return flags.Contains(flag); }

        /// <summary>Starts (or restarts) the scenario from the beginning.</summary>
        public void Start()
        {
            flags.Clear(); usedOptions.Clear(); usedInteractions.Clear(); decisions.Clear();
            LastOption = null;
            Distress = Clamp(Scenario.initial.distress);
            Trust = Clamp(Scenario.initial.trust);
            Sensory = Clamp(Scenario.initial.sensory);
            Enter(Scenario.startNodeId);
        }

        public List<DecisionOption> GetAvailableOptions()
        {
            var result = new List<DecisionOption>();
            if (CurrentNode == null || CurrentNode.kind != "decision") return result;
            foreach (var o in CurrentNode.options)
            {
                if (o.oneShot && usedOptions.Contains(o.id)) continue;
                if (!FlagsAllow(o.requiresFlags, o.blockedFlags)) continue;
                result.Add(o);
            }
            return result;
        }

        public DecisionRecord Choose(string optionId)
        {
            if (CurrentNode == null) throw new InvalidOperationException("Scenario has not been started.");
            var available = GetAvailableOptions();
            var option = available.FirstOrDefault(o => o.id == optionId);
            if (option == null)
                throw new InvalidOperationException("Option '" + optionId + "' is not available at node '" + CurrentNode.id + "'.");

            var node = CurrentNode;
            ApplyEffect(option.effects);
            usedOptions.Add(option.id);
            LastOption = option;

            var record = new DecisionRecord
            {
                Order = decisions.Count + 1,
                NodeId = node.id,
                Prompt = node.text,
                OptionId = option.id,
                ChosenText = option.text,
                Principle = ScenarioParse.ToPrinciple(option.principle),
                Alignment = ScenarioParse.ToAlignment(option.alignment),
                CriterionId = option.criterionId,
                Feedback = option.feedback,
                WaitSeconds = option.waitSeconds,
                DistressAfter = Distress, TrustAfter = Trust, SensoryAfter = Sensory
            };
            foreach (var alt in available)
            {
                if (alt == option) continue;
                record.Alternatives.Add(new AlternativeRecord
                {
                    Text = alt.text,
                    Principle = ScenarioParse.ToPrinciple(alt.principle),
                    Alignment = ScenarioParse.ToAlignment(alt.alignment),
                    Note = string.IsNullOrEmpty(alt.alternativeNote) ? alt.feedback : alt.alternativeNote
                });
            }
            decisions.Add(record);

            Enter(ResolveNext(option));
            return record;
        }

        public bool CanInteract(string interactionId)
        {
            if (CurrentNode == null || IsFinished) return false;
            InteractionDef def;
            if (!interactions.TryGetValue(interactionId ?? string.Empty, out def)) return false;
            if (def.oneShot && usedInteractions.Contains(def.id)) return false;
            return FlagsAllow(def.requiresFlags, def.blockedFlags);
        }

        public InteractionDef GetInteraction(string interactionId)
        {
            InteractionDef def;
            return interactions.TryGetValue(interactionId ?? string.Empty, out def) ? def : null;
        }

        /// <summary>Applies an environment interaction (e.g. turning the siren off). Returns null if unavailable.</summary>
        public DecisionRecord Interact(string interactionId)
        {
            if (!CanInteract(interactionId)) return null;
            var def = interactions[interactionId];
            ApplyEffect(def.effects);
            usedInteractions.Add(def.id);

            var record = new DecisionRecord
            {
                Order = decisions.Count + 1,
                NodeId = CurrentNode.id,
                Prompt = "Environment: " + def.prompt,
                OptionId = def.id,
                ChosenText = def.prompt,
                Principle = ScenarioParse.ToPrinciple(def.principle),
                Alignment = ScenarioParse.ToAlignment(def.alignment),
                CriterionId = def.criterionId,
                Feedback = def.feedback,
                IsEnvironmentInteraction = true,
                DistressAfter = Distress, TrustAfter = Trust, SensoryAfter = Sensory
            };
            decisions.Add(record);
            RaiseChanged();
            return record;
        }

        // ------------------------------------------------------------------

        string ResolveNext(DecisionOption option)
        {
            foreach (var b in option.branches)
            {
                if (!FlagsAllow(b.requiresFlags, b.blockedFlags)) continue;
                if (!b.stats.All(Holds)) continue;
                return b.next;
            }
            return option.next;
        }

        void Enter(string nodeId)
        {
            ScenarioNode node;
            if (!nodes.TryGetValue(nodeId ?? string.Empty, out node))
                throw new InvalidOperationException("Unknown node '" + nodeId + "'.");
            CurrentNode = node;
            ApplyEffect(node.onEnter);
            var handler = NodeEntered;
            if (handler != null) handler(node);
            RaiseChanged();
        }

        void ApplyEffect(Effect e)
        {
            if (e == null) return;
            Distress = Clamp(Distress + e.distress);
            Trust = Clamp(Trust + e.trust);
            Sensory = Clamp(Sensory + e.sensory);
            foreach (var f in e.setFlags) flags.Add(f);
            foreach (var f in e.clearFlags) flags.Remove(f);
        }

        bool FlagsAllow(List<string> requires, List<string> blocked)
        {
            foreach (var f in requires) if (!flags.Contains(f)) return false;
            foreach (var f in blocked) if (flags.Contains(f)) return false;
            return true;
        }

        bool Holds(StatCondition c)
        {
            int actual;
            switch ((c.stat ?? string.Empty).ToLowerInvariant())
            {
                case "distress": actual = Distress; break;
                case "trust": actual = Trust; break;
                case "sensory": actual = Sensory; break;
                default: throw new InvalidOperationException("Unknown stat '" + c.stat + "'.");
            }
            switch (c.op)
            {
                case "<": return actual < c.value;
                case "<=": return actual <= c.value;
                case ">": return actual > c.value;
                case ">=": return actual >= c.value;
                case "==": return actual == c.value;
                case "!=": return actual != c.value;
                default: throw new InvalidOperationException("Unknown operator '" + c.op + "'.");
            }
        }

        static int Clamp(int v) { return v < MinStat ? MinStat : (v > MaxStat ? MaxStat : v); }

        void RaiseChanged() { var h = Changed; if (h != null) h(); }
    }
}
