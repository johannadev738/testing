using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

static class Program
{
    static int Main()
    {
        var asm = typeof(SimSafe.Tests.SessionTests).Assembly;
        int pass = 0, fail = 0;
        foreach (var type in asm.GetTypes().Where(t => t.GetCustomAttribute<TestFixtureAttribute>() != null))
        {
            var setup = type.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);
            foreach (var test in type.GetMethods().Where(m => m.GetCustomAttribute<TestAttribute>() != null).OrderBy(m => m.Name))
            {
                var fixture = Activator.CreateInstance(type);
                var sw = Stopwatch.StartNew();
                try
                {
                    if (setup != null) setup.Invoke(fixture, null);
                    test.Invoke(fixture, null);
                    pass++;
                    Console.WriteLine("PASS  " + test.Name + "  (" + sw.ElapsedMilliseconds + " ms)");
                }
                catch (TargetInvocationException ex)
                {
                    fail++;
                    Console.WriteLine("FAIL  " + test.Name + "\n      " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
                }
            }
        }
        Console.WriteLine();
        Console.WriteLine("Passed: " + pass + "  Failed: " + fail);
        return fail == 0 ? 0 : 1;
    }
}
