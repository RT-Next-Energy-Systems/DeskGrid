using System.Runtime;
using Windows.Win32;

namespace DeskGrid.Diagnostics;

/// <summary>
/// Gives back the memory DeskGrid does not need while it sits idle: the managed heap is compacted and what it no longer uses goes
/// back to Windows, and the process's pages leave its working set (what is used again comes back page by page, a few milliseconds
/// for the first panel after a clean-up, measured). A panel that was opened takes about 90 MB of working set (the renderer, the
/// pictures, the code it ran); without this it stayed there until NotiLand's own, much later clean-up.
/// </summary>
internal static class MemoryTrim
{
    /// <summary>Runs the clean-up now. Call on the UI thread when nothing is being used.</summary>
    public static void Run()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

        // Aggressive also returns the memory the heap no longer uses to the system.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        PInvoke.SetProcessWorkingSetSize(PInvoke.GetCurrentProcess(), nuint.MaxValue, nuint.MaxValue);
    }
}
