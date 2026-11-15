using System.IO;
using System.Text.Json;
using SimSafe.Core;

namespace SimSafe.Tests
{
    // Harness equivalent of UnityScenarioSource.cs. Options mirror what Unity's JsonUtility accepts:
    // public fields, strict JSON (no comments or trailing commas).
    public static class ScenarioSource
    {
        public static ScenarioDefinition Load()
        {
            string dir = AppContext_BaseDir();
            string path = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "..", "Assets", "SimSafe", "Resources", "Scenarios", "residential_welfare_call.json"));
            var opts = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = false };
            return JsonSerializer.Deserialize<ScenarioDefinition>(File.ReadAllText(path), opts);
        }
        static string AppContext_BaseDir() { return System.AppContext.BaseDirectory; }
    }
}
