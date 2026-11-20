using BrimDeck.Core;

// DeskGrid build: the features that change another program's files or install a service with administrator rights are not
// part of this copy of BrimDeck (see third_party/BrimDeck/DESKGRID.md). These stand-ins keep the rest of the source as it is and
// answer "not available": no player is patched, no service is installed, no sensor is read through a driver.
namespace BrimDeck.Core
{
    public enum LibcefPatchState { Original, Patched, Partial, Unsupported }

    public static class KugouPatch
    {
        public const int Port = 12233;

        public static bool RestartDue(LibcefPatchState? state, bool fullControl, DateTime? started, DateTime? written) => false;
    }

    public static class NeteasePatch
    {
        public const int Port = 21631;
    }
}

namespace BrimDeck.Native
{
    internal static class KugouInstall
    {
        internal sealed record Location(string Launcher, string Library);

        public static Location? Find() => null;

        public static LibcefPatchState? State() => null;

        public static LibcefPatchState? State(Location location) => null;

        public static (LibcefPatchState? State, DateTime? Written) Inspect() => (null, null);

        public static Task ChangeFileAsync(bool patch, CancellationToken token) => Task.CompletedTask;

        public static Task EnableAsync(bool start, CancellationToken token) => Task.CompletedTask;

        public static bool Running() => false;

        public static (string Key, DateTime? Started) Processes() => ("", null);
    }

    internal static class NeteaseInstall
    {
        internal sealed record Location(string Executable, string Library);

        public static Location? Find() => null;

        public static LibcefPatchState? State() => null;

        public static LibcefPatchState? State(Location location) => null;

        public static Task ChangeFileAsync(bool patch, CancellationToken token) => Task.CompletedTask;

        public static Task EnableAsync(bool start, CancellationToken token) => Task.CompletedTask;

        public static bool Running() => false;
    }

    internal sealed class SensorClient
    {
        public SensorReport? Last => null;

        public SensorReport? Read(bool wanted, double now) => null;

        public static SensorReport? ReadOnce() => null;
    }
}
