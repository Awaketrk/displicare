using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Displicare.Tests
{
    /// <summary>Marks a public static void method as a test.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertFailedException : Exception
    {
        public AssertFailedException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertFailedException(message);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertFailedException(string.Format("{0}: expected {1}, got {2}", what, expected, actual));
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new AssertFailedException(string.Format("{0}: expected {1} ±{2}, got {3}", what, expected, tolerance, actual));
        }
    }

    /// <summary>
    /// Minimal dependency-free test runner: runs every [Test] method in this assembly and
    /// exits with code 1 if any fail, so `msbuild` + running the exe works locally and in CI.
    /// </summary>
    public static class TestRunner
    {
        public static int Main()
        {
            var tests = Assembly.GetExecutingAssembly().GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.GetCustomAttributes(typeof(TestAttribute), false).Length > 0)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();

            int failed = 0;
            foreach (var test in tests)
            {
                string name = test.DeclaringType.Name + "." + test.Name;
                try
                {
                    test.Invoke(null, null);
                    Console.WriteLine("  PASS  " + name);
                }
                catch (TargetInvocationException ex)
                {
                    failed++;
                    var inner = ex.InnerException;
                    Console.WriteLine("  FAIL  " + name);
                    Console.WriteLine("        " + (inner is AssertFailedException ? inner.Message : inner.ToString()));
                }
            }

            Console.WriteLine();
            Console.WriteLine("{0} passed, {1} failed, {2} total", tests.Count - failed, failed, tests.Count);
            return failed == 0 ? 0 : 1;
        }
    }
}
