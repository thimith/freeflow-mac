using System.Runtime.InteropServices;
using NAudio.Wave;

namespace FreeFlow;

/// Global low-level keyboard hook: detects the push-to-talk key anywhere in Windows.
sealed class KeyboardHook : IDisposable
{
    public Action<bool> TriggerDown;   // arg: Shift held → Command mode
    public Action TriggerUp, CommandModifier, OtherKey, Escape;

    readonly SynchronizationContext ui = SynchronizationContext.Current;
    readonly Native.LowLevelKeyboardProc proc;
    IntPtr hook;
    bool active, maskWin;
    bool lctrl, rctrl, lwin, rwin, lshift, rshift, ralt, caps;

    public KeyboardHook() { proc = Callback; }

    public void Install()
    {
        hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    void Post(Action a) { if (a != null) ui.Post(_ => a(), null); }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            if ((k.flags & Native.LLKHF_INJECTED) == 0)
            {
                int msg = (int)wParam;
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                if (Handle(k.vkCode, down)) return (IntPtr)1;   // swallow
            }
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    /// Returns true to swallow the key.
    bool Handle(uint vk, bool down)
    {
        var trig = Settings.Current.Trigger;
        bool isModifier = true;
        switch (vk)
        {
            case Native.VK_LCONTROL: lctrl = down; break;
            case Native.VK_RCONTROL: rctrl = down; break;
            case Native.VK_LWIN: lwin = down; break;
            case Native.VK_RWIN: rwin = down; break;
            case Native.VK_LSHIFT: lshift = down; break;
            case Native.VK_RSHIFT: rshift = down; break;
            case Native.VK_RMENU: ralt = down; break;
            case Native.VK_LMENU: break;
            case 0x14: if (trig == TriggerKey.CapsLock) caps = down; else isModifier = false; break;
            default: isModifier = false; break;
        }

        if (vk == 0x1B && down) Post(Escape);

        // Releasing Win after Ctrl+Win would open the Start menu — inject a dummy key to prevent it.
        if (maskWin && !down && (vk == Native.VK_LWIN || vk == Native.VK_RWIN))
        {
            Native.Send(Native.Key(Native.VK_MASK, false), Native.Key(Native.VK_MASK, true));
            if (!lwin && !rwin) maskWin = false;
        }

        bool shift = lshift || rshift;
        bool now = trig switch
        {
            TriggerKey.CtrlWin => (lctrl || rctrl) && (lwin || rwin),
            TriggerKey.RightCtrl => rctrl,
            TriggerKey.RightAlt => ralt,
            TriggerKey.CapsLock => caps,
            _ => false,
        };

        if (now && !active)
        {
            active = true;
            if (trig == TriggerKey.CtrlWin) maskWin = true;
            Post(() => TriggerDown?.Invoke(shift));
        }
        else if (!now && active)
        {
            active = false;
            Post(TriggerUp);
        }
        else if (active && down)
        {
            if (vk == Native.VK_LSHIFT || vk == Native.VK_RSHIFT) Post(CommandModifier);
            else if (!isModifier && vk != 0x1B) Post(OtherKey);   // e.g. Ctrl+Win+→ : a real shortcut
        }

        return trig == TriggerKey.CapsLock && vk == 0x14;   // don't toggle Caps Lock
    }
}

/// System-wide combos (Ctrl+Alt+Space etc.) via RegisterHotKey.
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    public Action Toggle, PasteLast, Translate;
    Combo regToggle = Combo.None, regPaste = Combo.None, regTranslate = Combo.None;
    bool registered;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
        Register();
        Settings.Changed += Register;
    }

    public void Register()
    {
        var s = Settings.Current;
        if (registered && regToggle == s.ToggleCombo && regPaste == s.PasteCombo && regTranslate == s.TranslateCombo) return;
        Native.UnregisterHotKey(Handle, 1);
        Native.UnregisterHotKey(Handle, 2);
        Native.UnregisterHotKey(Handle, 3);
        if (s.TranslateCombo != Combo.None)
        {
            var (m, vk) = Labels.Keys(s.TranslateCombo);
            Native.RegisterHotKey(Handle, 3, m | Native.MOD_NOREPEAT, vk);
        }
        if (s.ToggleCombo != Combo.None)
        {
            var (m, vk) = Labels.Keys(s.ToggleCombo);
            Native.RegisterHotKey(Handle, 1, m | Native.MOD_NOREPEAT, vk);
        }
        if (s.PasteCombo != Combo.None)
        {
            var (m, vk) = Labels.Keys(s.PasteCombo);
            Native.RegisterHotKey(Handle, 2, m | Native.MOD_NOREPEAT, vk);
        }
        regToggle = s.ToggleCombo;
        regPaste = s.PasteCombo;
        regTranslate = s.TranslateCombo;
        registered = true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            if (m.WParam == (IntPtr)1) Toggle?.Invoke();
            else if (m.WParam == (IntPtr)2) PasteLast?.Invoke();
            else if (m.WParam == (IntPtr)3) Translate?.Invoke();
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Settings.Changed -= Register;
        Native.UnregisterHotKey(Handle, 1);
        Native.UnregisterHotKey(Handle, 2);
        Native.UnregisterHotKey(Handle, 3);
        DestroyHandle();
    }
}

/// Records 16 kHz mono 16-bit WAV (Whisper's native format).
sealed class Recorder
{
    public Action<float> Level;
    WaveInEvent wave;
    WaveFileWriter writer;
    readonly object sync = new();
    float peak;
    string path;

    public static List<string> Devices()
    {
        var list = new List<string>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++) list.Add(WaveInEvent.GetCapabilities(i).ProductName);
        return list;
    }

    public string Start()
    {
        path = Path.Combine(Path.GetTempPath(), $"freeflow-{Guid.NewGuid():N}.wav");
        int dev = Settings.Current.MicDevice;
        if (dev >= WaveInEvent.DeviceCount) dev = -1;
        wave = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 30, DeviceNumber = dev };
        writer = new WaveFileWriter(path, wave.WaveFormat);
        peak = 0;
        wave.DataAvailable += OnData;
        wave.StartRecording();
        return path;
    }

    void OnData(object sender, WaveInEventArgs e)
    {
        lock (sync)
        {
            if (writer == null) return;
            writer.Write(e.Buffer, 0, e.BytesRecorded);
        }
        double sum = 0;
        int n = e.BytesRecorded / 2;
        for (int i = 0; i < n; i++)
        {
            double s = BitConverter.ToInt16(e.Buffer, i * 2) / 32768.0;
            sum += s * s;
        }
        double rms = n > 0 ? Math.Sqrt(sum / n) : 0;
        double db = 20 * Math.Log10(Math.Max(rms, 1e-6));
        float level = (float)Math.Clamp((db + 50) / 45, 0, 1);
        if (level > peak) peak = level;
        Level?.Invoke(level);
    }

    public (double duration, float peak) Stop()
    {
        double dur = 0;
        try { wave?.StopRecording(); } catch { }
        lock (sync)
        {
            if (writer != null)
            {
                dur = writer.TotalTime.TotalSeconds;
                writer.Dispose();
                writer = null;
            }
        }
        if (wave != null) { wave.DataAvailable -= OnData; wave.Dispose(); wave = null; }
        return (dur, peak);
    }
}
