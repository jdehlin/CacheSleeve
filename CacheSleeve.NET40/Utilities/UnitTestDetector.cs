using System;

namespace CacheSleeve.Utilities
{
    static class UnitTestDetector
    {
        private static readonly bool RunningFromXUnit;

        static UnitTestDetector()
        {
            foreach (var assem in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assem.FullName.ToLowerInvariant().StartsWith("xunit"))
                {
                    RunningFromXUnit = true;
                    break;
                }
            }
        }

        public static bool IsRunningFromXunit
        {
            get { return RunningFromXUnit; }
        }
    }
}