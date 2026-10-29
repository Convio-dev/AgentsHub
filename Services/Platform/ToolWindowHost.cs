#if WINDOWS
namespace AgentsHub.Services.Platform;

/// <summary>
/// A window that drives a page in the background and must never appear in Alt+Tab.
///
/// The engines keep their host window shown — Chromium throttles a window that is not shown, and a
/// throttled page stops producing tokens — so the window is parked off the desktop instead. That
/// left a problem: `ShowInTaskbar` was toggled whenever the window was brought forward, and WinForms
/// sets `WS_EX_APPWINDOW` when it becomes true but does not reliably clear it when it goes back to
/// false. One click on "Open window" therefore made that host a permanent Alt+Tab entry.
///
/// `WS_EX_TOOLWINDOW` is the only style that keeps a window out of Alt+Tab, and it is applied here at
/// handle creation, where nothing later can undo it.
/// </summary>
internal sealed class ToolWindowHost : Form
{
    /// <summary>Shown without stealing focus: a background host must not interrupt typing.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;

            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_APPWINDOW = 0x00040000;

            cp.ExStyle |= WS_EX_TOOLWINDOW;
            cp.ExStyle &= ~WS_EX_APPWINDOW;

            return cp;
        }
    }
}
#endif
