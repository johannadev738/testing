// Unity-side loader for the tests. The standalone harness (tools/harness) supplies its own
// ScenarioSource so that SessionTests.cs can be compiled both in Unity and outside it.
using System.IO;
using SimSafe.Core;
using UnityEngine;

namespace SimSafe.Tests
{
    public static class ScenarioSource
    {
        public static ScenarioDefinition Load()
        {
            string path = Path.Combine(Application.dataPath, "SimSafe/Resources/Scenarios/residential_welfare_call.json");
            return JsonUtility.FromJson<ScenarioDefinition>(File.ReadAllText(path));
        }
    }
}
