namespace FreeFlow;

public enum Phase { Listening, HandsFree, Command, Transcribing, Polishing, Thinking, Translating, Done, Message }

/// Status display. Everything shows inside the floating bubble; this thin wrapper keeps the rest of the app
/// unchanged — and guarantees that a drawing problem can never interrupt a dictation.
sealed class Overlay
{
    public DockBar Dock;

    static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void PushLevel(float l) => Safe(() => Dock?.PushLevel(l));
    public void ShowPhase(Phase p) => Safe(() => Dock?.ShowPhase(p));
    public void Set(Phase p, string msg = "") => Safe(() => Dock?.SetPhase(p, msg));
    public void Flash(string message) => Safe(() => Dock?.SetPhase(Phase.Message, message));
    public void HidePill() => Safe(() => Dock?.GoIdle());
}
