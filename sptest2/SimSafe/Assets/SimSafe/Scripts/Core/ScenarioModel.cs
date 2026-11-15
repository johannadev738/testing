using System;
using System.Collections.Generic;

namespace SimSafe.Core
{
    // ---------------------------------------------------------------------
    // Scenario data model.
    //
    // These classes are plain serialisable data so the same JSON file can be
    // read by Unity's JsonUtility at runtime and by System.Text.Json in the
    // standalone test harness. Rules for staying compatible with both:
    //   * public fields only, no properties, no dictionaries
    //   * enums are stored as strings and parsed through ScenarioParse
    //   * lists are initialised so missing JSON keys give empty lists
    //
    // No UnityEngine references are allowed in this assembly.
    // ---------------------------------------------------------------------

    public enum Principle { None, Slow, Lower, Observe, Wait }
    public enum Alignment { Neutral, Aligned, Mixed, Misaligned }
    public enum OutcomeTone { None, Positive, Mixed, Difficult }

    public static class ScenarioParse
    {
        public static bool TryPrinciple(string s, out Principle p)
        {
            switch ((s ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "": p = Principle.None; return true;
                case "S": p = Principle.Slow; return true;
                case "L": p = Principle.Lower; return true;
                case "O": p = Principle.Observe; return true;
                case "W": p = Principle.Wait; return true;
                default: p = Principle.None; return false;
            }
        }

        public static bool TryAlignment(string s, out Alignment a)
        {
            switch ((s ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "neutral": a = Alignment.Neutral; return true;
                case "aligned": a = Alignment.Aligned; return true;
                case "mixed": a = Alignment.Mixed; return true;
                case "misaligned": a = Alignment.Misaligned; return true;
                default: a = Alignment.Neutral; return false;
            }
        }

        public static bool TryTone(string s, out OutcomeTone t)
        {
            switch ((s ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "": t = OutcomeTone.None; return true;
                case "positive": t = OutcomeTone.Positive; return true;
                case "mixed": t = OutcomeTone.Mixed; return true;
                case "difficult": t = OutcomeTone.Difficult; return true;
                default: t = OutcomeTone.None; return false;
            }
        }

        public static Principle ToPrinciple(string s) { TryPrinciple(s, out var p); return p; }
        public static Alignment ToAlignment(string s) { TryAlignment(s, out var a); return a; }
        public static OutcomeTone ToTone(string s) { TryTone(s, out var t); return t; }

        public static string Letter(Principle p)
        {
            switch (p)
            {
                case Principle.Slow: return "S";
                case Principle.Lower: return "L";
                case Principle.Observe: return "O";
                case Principle.Wait: return "W";
                default: return "-";
            }
        }

        public static string Label(Principle p)
        {
            switch (p)
            {
                case Principle.Slow: return "Slow Down";
                case Principle.Lower: return "Lower Sensory";
                case Principle.Observe: return "Observe Scene";
                case Principle.Wait: return "Wait Patiently";
                default: return "General";
            }
        }

        public static string Label(Alignment a)
        {
            switch (a)
            {
                case Alignment.Aligned: return "Aligned";
                case Alignment.Mixed: return "Mixed";
                case Alignment.Misaligned: return "Not aligned";
                default: return "Neutral";
            }
        }
    }

    [Serializable]
    public class StatBlock
    {
        public int distress;
        public int trust;
        public int sensory;
    }

    [Serializable]
    public class Effect
    {
        public int distress;
        public int trust;
        public int sensory;
        public List<string> setFlags = new List<string>();
        public List<string> clearFlags = new List<string>();
    }

    [Serializable]
    public class StatCondition
    {
        public string stat;   // distress | trust | sensory
        public string op;     // < <= > >= == !=
        public int value;
    }

    [Serializable]
    public class Branch
    {
        public List<string> requiresFlags = new List<string>();
        public List<string> blockedFlags = new List<string>();
        public List<StatCondition> stats = new List<StatCondition>();
        public string next;
    }

    [Serializable]
    public class DecisionOption
    {
        public string id;
        public string text;
        public string principle;      // S | L | O | W | ""
        public string alignment;      // aligned | mixed | misaligned | neutral
        public string criterionId;
        public string response;       // optional in-world reply shown after choosing
        public string feedback;       // educational feedback (also used for alternatives in the debrief)
        public string alternativeNote;
        public Effect effects = new Effect();
        public float waitSeconds;     // processing-time pause run by the UI before the choice resolves
        public bool oneShot;
        public List<string> requiresFlags = new List<string>();
        public List<string> blockedFlags = new List<string>();
        public string next;
        public List<Branch> branches = new List<Branch>();   // first match wins, else 'next'
    }

    [Serializable]
    public class ScenarioNode
    {
        public string id;
        public string kind;           // decision | outcome
        public string trigger;        // auto | interact
        public string stage;          // optional staging hint for the 3D layer (near_sam, dana_comes)
        public string speakerId;
        public string speakerName;
        public string text;
        public string hint;
        public Effect onEnter = new Effect();
        public List<DecisionOption> options = new List<DecisionOption>();
        public string outcomeTone;
        public string outcomeTitle;
        public string outcomeSummary;
    }

    [Serializable]
    public class InteractionDef
    {
        public string id;
        public string prompt;
        public string principle;
        public string alignment;
        public string criterionId;
        public string feedback;
        public Effect effects = new Effect();
        public bool oneShot;
        public List<string> requiresFlags = new List<string>();
        public List<string> blockedFlags = new List<string>();
    }

    [Serializable]
    public class Criterion
    {
        public string id;
        public string principle;
        public string title;
        public string description;
        public string suggestion;
    }

    [Serializable]
    public class ObjectiveDef
    {
        public string id;
        public string text;
        public List<string> flags = new List<string>();   // complete when all are set
    }

    [Serializable]
    public class ScenarioDefinition
    {
        public string id;
        public string title;
        public string summary;
        public string status;
        public string disclaimer;
        public string nonGuaranteeNote;
        public string dispatchBriefing;
        public string startNodeId;
        public StatBlock initial = new StatBlock();
        public List<Criterion> criteria = new List<Criterion>();
        public List<ObjectiveDef> objectives = new List<ObjectiveDef>();
        public List<InteractionDef> interactions = new List<InteractionDef>();
        public List<ScenarioNode> nodes = new List<ScenarioNode>();
    }
}
