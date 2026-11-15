using System;
using System.Collections;

// Minimal stand-in for the NUnit 3 API surface that SessionTests.cs uses.
namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class TestFixtureAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SetUpAttribute : Attribute { }

    public sealed class AssertionException : Exception { public AssertionException(string m) : base(m) { } }

    public static class Assert
    {
        static void Fail(string message, string detail) { throw new AssertionException(string.IsNullOrEmpty(message) ? detail : message + " (" + detail + ")"); }
        public static void Fail(string message) { throw new AssertionException(message); }
        public static void IsTrue(bool c, string m = null) { if (!c) Fail(m, "expected true"); }
        public static void IsFalse(bool c, string m = null) { if (c) Fail(m, "expected false"); }
        public static void IsNull(object o, string m = null) { if (o != null) Fail(m, "expected null"); }
        public static void IsNotNull(object o, string m = null) { if (o == null) Fail(m, "expected not null"); }
        public static void AreEqual(object e, object a, string m = null) { if (!Equals(e, a)) Fail(m, "expected <" + e + "> but was <" + a + ">"); }
        static int Cmp(object a, object b) { return Convert.ToDouble(a).CompareTo(Convert.ToDouble(b)); }
        public static void Greater(object a, object b, string m = null) { if (Cmp(a, b) <= 0) Fail(m, a + " is not greater than " + b); }
        public static void GreaterOrEqual(object a, object b, string m = null) { if (Cmp(a, b) < 0) Fail(m, a + " is not >= " + b); }
        public static void Less(object a, object b, string m = null) { if (Cmp(a, b) >= 0) Fail(m, a + " is not less than " + b); }
        public static void LessOrEqual(object a, object b, string m = null) { if (Cmp(a, b) > 0) Fail(m, a + " is not <= " + b); }
        public static void Throws<T>(Action code) where T : Exception
        {
            try { code(); }
            catch (T) { return; }
            catch (Exception ex) { Fail(null, "expected " + typeof(T).Name + " but got " + ex.GetType().Name); }
            Fail(null, "expected " + typeof(T).Name + " but nothing was thrown");
        }
    }

    public static class StringAssert
    {
        public static void Contains(string expected, string actual)
        {
            if (actual == null || actual.IndexOf(expected, StringComparison.Ordinal) < 0)
                throw new AssertionException("expected string containing <" + expected + "> but was <" + actual + ">");
        }
    }
}
