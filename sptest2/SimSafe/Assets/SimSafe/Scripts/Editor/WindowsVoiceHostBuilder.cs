using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SimSafe.EditorTools
{
    public static class WindowsVoiceHostBuilder
    {
        [MenuItem("SimSafe/Voice/Build Windows Offline Host")]
        public static void Build()
        {
#if UNITY_EDITOR_WIN
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string compiler = Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
            string assemblyRoot = Path.Combine(windows, "Microsoft.NET", "assembly", "GAC_MSIL", "System.Speech");
            string[] assemblies = Directory.Exists(assemblyRoot)
                ? Directory.GetFiles(assemblyRoot, "System.Speech.dll", SearchOption.AllDirectories) : new string[0];
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tools", "voice-host", "WindowsVoiceHost.cs"));
            if (!File.Exists(compiler) || assemblies.Length == 0 || !File.Exists(source))
                throw new InvalidOperationException("Installed Windows .NET Framework compiler/System.Speech and tools/voice-host source are required. Nothing was installed.");
            string output = Path.Combine(Application.streamingAssetsPath, "SimSafeVoice", "WindowsVoiceHost.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var start = new ProcessStartInfo(compiler,
                "/nologo /target:exe /optimize+ /platform:anycpu /reference:" + Quote(assemblies[0]) + " /out:" + Quote(output) + " " + Quote(source))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20000)) { process.Kill(); throw new TimeoutException("Offline speech host compiler timed out."); }
                if (process.ExitCode != 0) throw new InvalidOperationException("Offline speech host compilation failed. Run tools/voice-host/Build.ps1 for compiler diagnostics.");
                System.Threading.Tasks.Task.WaitAll(stdout, stderr);
            }
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("Built local Windows offline speech host. No microphone or external service was used.");
#else
            throw new PlatformNotSupportedException("The offline speech host requires Windows.");
#endif
        }

        static string Quote(string value) => "\"" + value.Replace("\"", "") + "\"";
    }
}
