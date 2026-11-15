using System.Collections.Generic;
using SimSafe.Core;
using UnityEngine;

namespace SimSafe
{
    /// <summary>Loads and validates every scenario JSON under Resources/Scenarios. Works on WebGL (no file IO).</summary>
    public static class ScenarioLibrary
    {
        public static List<ScenarioDefinition> LoadAll(List<string> problems)
        {
            var result = new List<ScenarioDefinition>();
            foreach (var asset in Resources.LoadAll<TextAsset>("Scenarios"))
            {
                ScenarioDefinition def = null;
                try { def = JsonUtility.FromJson<ScenarioDefinition>(asset.text); }
                catch (System.Exception ex) { problems.Add(asset.name + ": could not parse JSON (" + ex.Message + ")"); }
                if (def == null) continue;
                var errors = ScenarioValidator.Validate(def);
                if (errors.Count > 0) { foreach (var e in errors) problems.Add(asset.name + ": " + e); continue; }
                result.Add(def);
            }
            return result;
        }
    }
}
