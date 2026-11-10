using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace DeskGrid.Island;

/// <summary>Whether a full-screen game, video or presentation is up, so the island stays out of its way.</summary>
internal static class FullscreenGuard
{
    /// <summary>
    /// Which of the Shell's "user notification states" mean a full-screen app: <c>RUNNING_D3D_FULL_SCREEN</c> and
    /// <c>PRESENTATION_MODE</c>. <c>BUSY</c> is not one: Windows also reports it for a maximized or borderless window and
    /// other focus states, which would hide the island all the time. Quiet time (focus assist) is not one either:
    /// the island is not a Windows notification.
    /// </summary>
    internal static bool Hides(QUERY_USER_NOTIFICATION_STATE state) => state is
        QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
        or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;

    /// <summary>Asks the Shell now; a failed answer counts as "no full-screen app".</summary>
    public static bool IsActive() => PInvoke.SHQueryUserNotificationState(out var state).Succeeded && Hides(state);
}
