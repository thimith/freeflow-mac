namespace FreeFlow;

public enum Phase { Listening, HandsFree, Command, Transcribing, Polishing, Thinking, Translating, Done, Message }

/// Status display. Everything now shows inside the floating bubble (one place, smooth edges);
/// this thin wrapper keeps the rest of the app unchanged.
sealed class Overlay
{
    public DockBar Dock;

    public void PushLevel(float l) => Dock?.PushLevel(l);
    public void ShowPhase(Phase p) => Dock?.ShowPhase(p);
    public void Set(Phase p, string msg = "") => Dock?.SetPhase(p, msg);
    public void Flash(string message) => Dock?.SetPhase(Phase.Message, message);
    public void HidePill() => Dock?.GoIdle();
}
