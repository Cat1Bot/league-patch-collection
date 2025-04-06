using System.Diagnostics;

namespace RiotApiLib
{
    internal static class ProcessChecker
    {
        internal static bool IsProcessRunning(params string[] processNames)
        {
            foreach (var name in processNames)
            {
                if (Process.GetProcessesByName(name).Length != 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}