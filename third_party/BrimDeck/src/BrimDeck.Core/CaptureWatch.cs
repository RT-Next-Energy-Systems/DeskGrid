namespace BrimDeck.Core;

// DeskGrid change: tells a capture that cannot hear the player from one that has only a quiet moment. The level bars read the player's
// audio through a capture of one process (ProcessLoopbackCapture), chosen once. When the sound comes from another process of the
// player (Apple Music has more than one), or the player's output is one a loopback capture gets only silence from (an exclusive or
// protected output), the capture stays silent while the session meters still show sound, and the bars stayed flat for good.
public sealed class CaptureWatch
{
    // How long the meter must hear sound while the capture is silent before the capture counts as deaf.
    public const double Grace = .7;
    // The session meter's level (0 to 1) that counts as sound: above the noise of an idle session.
    public const float Heard = .02f;
    private double _since = double.NaN;

    private volatile bool _deaf;
    public bool Deaf => _deaf;

    // captureLoud: the capture delivered sound a moment ago. sessionPeak: the loudest session meter of the player.
    // Returns whether Deaf changed.
    public bool Update(double now, bool captureLoud, float sessionPeak)
    {
        bool before = Deaf;
        if (captureLoud || sessionPeak < Heard) { _since = double.NaN; _deaf = false; }
        else
        {
            if (double.IsNaN(_since)) _since = now;
            if (now - _since >= Grace) _deaf = true;
        }
        return Deaf != before;
    }

    public void Reset() { _since = double.NaN; _deaf = false; }
}
