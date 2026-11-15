// The listening approach (a second Shell_TrayWnd that stays first in z-order, forwards everything to Explorer,
// and asks apps to re-add their icons with TaskbarCreated) and the message-forwarding rules are adapted from
// ManagedShell's TrayService (https://github.com/cairoshell/ManagedShell), Apache License 2.0.
// See THIRD-PARTY-NOTICES.md.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnowDock.Native;
using static SnowDock.Widgets.Tray.TrayNative;

namespace SnowDock.Widgets.Tray;

/// <summary>One Shell_NotifyIcon call, as an app sent it. Null/0 fields were not part of the call.</summary>
internal sealed record TrayMessage(int Message, IntPtr HWnd, uint UID, int Flags, uint CallbackMessage, ImageSource? Icon,
    string Tip, bool? Hidden, uint Version, Guid Guid);

/// <summary>
/// Hears what apps tell the notification area while Explorer keeps running it. Shell_NotifyIcon sends its data to
/// the first top-level "Shell_TrayWnd" window in z-order, so this keeps a hidden window of that class just above
/// Explorer's, reads each call, and passes every message on to Explorer unchanged (Explorer's answer goes back to
/// the app). Nothing is loaded into Explorer. The window lives on its own thread so a busy dock never stalls
/// other apps' tray calls.
/// </summary>
internal sealed class TrayHost
{
    private const string TrayClass = "Shell_TrayWnd", NotifyClass = "TrayNotifyWnd";
    private const int TrayData = 1;              // COPYDATASTRUCT.dwData of a Shell_NotifyIcon call
    private const uint ForwardTimeoutMs = 4000;
    // Explorer posts this to the taskbar when it restarts; forwarding it with SendMessage crash-loops recent Explorer.
    private const uint PostOnly = WM_USER + 372;

    private readonly WndProc _proc;             // held so the GC never collects what Windows calls
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private IntPtr _tray, _notify, _explorer;
    private NativeMethods.RECT _mirrored;
    private uint _taskbarCreated;

    /// <summary>Raised on the listener thread for every call Explorer accepted (and every delete).</summary>
    public event Action<TrayMessage>? Message;
    /// <summary>Explorer restarted: its new taskbar starts empty.</summary>
    public event Action? ExplorerRestarted;

    public TrayHost() => _proc = Proc;

    public bool Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SnowDock tray listener" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(3000);
        return _tray != IntPtr.Zero;
    }

    /// <summary>Close the window and wait for the thread, so a later listener can register the classes again.</summary>
    public void Stop()
    {
        if (_tray != IntPtr.Zero) PostMessage(_tray, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(2000);
    }

    private void Run()
    {
        var hInstance = GetModuleHandle(null);
        try
        {
            Register(TrayClass, hInstance);
            Register(NotifyClass, hInstance);
            _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            _tray = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, TrayClass, "", WS_POPUP | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            // If the dock runs as administrator, ordinary apps' tray calls must still get through (or they'd fail).
            if (_tray != IntPtr.Zero) ChangeWindowMessageFilterEx(_tray, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
            if (_tray != IntPtr.Zero)
                _notify = CreateWindowEx(0, NotifyClass, "", WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                    0, 0, 0, 0, _tray, IntPtr.Zero, hInstance, IntPtr.Zero);
        }
        catch (Exception ex) { AppPaths.Log($"Tray listener: setup failed: {ex.Message}"); }
        _ready.Set();
        if (_tray == IntPtr.Zero)
        {
            AppPaths.Log($"Tray listener: no window ({Marshal.GetLastWin32Error()})");
            return;
        }

        Watch();
        SetTimer(_tray, (UIntPtr)1, 100, IntPtr.Zero);
        AskAppsToReAdd();
        AppPaths.Log("Tray listener: started");
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnregisterClass(NotifyClass, hInstance);
        UnregisterClass(TrayClass, hInstance);
        AppPaths.Log("Tray listener: stopped");
    }

    private void Register(string name, IntPtr hInstance)
    {
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInstance, lpszClassName = name
        };
        if (RegisterClassEx(ref wc) != 0) return;
        // Left over from an earlier listener (its window procedure is gone): replace it.
        if (Marshal.GetLastWin32Error() == 1410 /* ERROR_CLASS_ALREADY_EXISTS */ && UnregisterClass(name, hInstance) && RegisterClassEx(ref wc) != 0) return;
        AppPaths.Log($"Tray listener: RegisterClassEx {name} failed ({Marshal.GetLastWin32Error()})");
    }

    private IntPtr Proc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // TrayNotifyWnd (and the tray window while it's being created) only need default handling.
        if (hWnd != _tray || _tray == IntPtr.Zero) return DefWindowProc(hWnd, msg, wParam, lParam);
        try
        {
            switch (msg)
            {
                case WM_COPYDATA: return OnCopyData(wParam, lParam);
                case WM_TIMER: Watch(); return IntPtr.Zero;
                case WM_WINDOWPOSCHANGED:
                    // Something tried to show "the taskbar" and found us: stay invisible.
                    if (((uint)Marshal.ReadInt32(lParam, IntPtr.Size * 2 + 16) & SWP_SHOWWINDOW) != 0)
                        NativeMethods.ShowWindowAsync(_tray, NativeMethods.SW_HIDE);
                    break;
                case WM_CLOSE: DestroyWindow(_tray); return IntPtr.Zero;
                case WM_DESTROY: _tray = IntPtr.Zero; PostQuitMessage(0); return IntPtr.Zero;
            }
            if (msg == _taskbarCreated && _taskbarCreated != 0) { OnTaskbarCreated(); return IntPtr.Zero; }
            // Commands and private messages other programs send "the taskbar" belong to Explorer. (Registered
            // messages are left alone: they're usually broadcasts Explorer gets itself.)
            if (msg == WM_COMMAND || (msg >= WM_USER && msg < 0xC000)) return Forward(msg, wParam, lParam);
        }
        catch (Exception ex) { AppPaths.Log($"Tray listener: message {msg:X} failed: {ex.Message}"); }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr OnCopyData(IntPtr wParam, IntPtr lParam)
    {
        TrayMessage? m = null;
        try { m = Parse(lParam); }
        catch (Exception ex) { AppPaths.Log($"Tray listener: unreadable icon data: {ex.Message}"); }

        // Explorer decides first, for every kind of data (icons, appbars, icon-rect queries).
        var explorer = Explorer();
        IntPtr result = explorer == IntPtr.Zero ? (IntPtr)(m != null ? 1 : 0) : Send(explorer, WM_COPYDATA, wParam, lParam);
        // Apps answering our TaskbarCreated re-add icons Explorer still has; Explorer refuses a second add,
        // so hand it over as an update instead (what the app means), rather than leave the app thinking it failed.
        if (m is { Message: NIM_ADD } && result == IntPtr.Zero && explorer != IntPtr.Zero)
            result = SendAsModify(explorer, wParam, lParam);

        if (m != null && (result != IntPtr.Zero || m.Message == NIM_DELETE))
        {
            try { Message?.Invoke(m); }
            catch (Exception ex) { AppPaths.Log($"Tray listener: bookkeeping failed: {ex.Message}"); }
        }
        return result;
    }

    /// <summary>SHELLTRAYDATA: a signature, the NIM_ message, then NOTIFYICONDATAW with 32-bit handles.</summary>
    private static TrayMessage? Parse(IntPtr lParam)
    {
        var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        if (cds.dwData != (IntPtr)TrayData || cds.lpData == IntPtr.Zero || cds.cbData < 32) return null;
        IntPtr p = cds.lpData;
        int n = cds.cbData;
        int I(int offset) => offset + 4 <= n ? Marshal.ReadInt32(p, offset) : 0;

        int flags = I(20);
        string tip = "";
        if ((flags & NIF_TIP) != 0 && n >= 32 + 256)
        {
            tip = Marshal.PtrToStringUni(p + 32, 128);
            int end = tip.IndexOf('\0');
            if (end >= 0) tip = tip[..end];
        }
        bool? hidden = null;
        if ((flags & NIF_STATE) != 0 && (I(292) & NIS_HIDDEN) != 0) hidden = (I(288) & NIS_HIDDEN) != 0;
        var guid = (flags & NIF_GUID) != 0 && n >= 944 + 16 ? Marshal.PtrToStructure<Guid>(p + 944) : Guid.Empty;
        // Handles travel as 32 bits; 64-bit Windows sign-extends them.
        var icon = (flags & NIF_ICON) != 0 ? FromHIcon((IntPtr)I(28)) : null;
        return new TrayMessage(I(4), (IntPtr)I(12), (uint)I(16), flags, (uint)I(24), icon, tip, hidden, (uint)I(808), guid);
    }

    /// <summary>A frozen copy of the app's icon, made now: the app may destroy the handle right after the call.</summary>
    private static ImageSource? FromHIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var pbgra = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            int w = pbgra.PixelWidth, h = pbgra.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            pbgra.CopyPixels(px, stride, 0);
            var copy = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
            copy.Freeze();
            return copy;
        }
        catch { return null; }
    }

    private IntPtr Explorer()
    {
        if (_explorer == IntPtr.Zero || !NativeMethods.IsWindow(_explorer)) _explorer = TaskbarController.ExplorerTray();
        return _explorer;
    }

    private IntPtr Forward(uint msg, IntPtr wParam, IntPtr lParam)
    {
        var explorer = Explorer();
        if (explorer == IntPtr.Zero) return IntPtr.Zero;
        // Keep the sender's style: posted stays posted, so nobody waits on a message that was never waited on.
        if (msg == PostOnly || (InSendMessageEx(IntPtr.Zero) & ISMEX_SEND) == 0)
        {
            PostMessage(explorer, msg, wParam, lParam);
            return IntPtr.Zero;
        }
        return Send(explorer, msg, wParam, lParam);
    }

    private static IntPtr Send(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        SendMessageTimeout(hWnd, msg, wParam, lParam, SMTO_ABORTIFHUNG, ForwardTimeoutMs, out var r) != IntPtr.Zero ? r : IntPtr.Zero;

    private static IntPtr SendAsModify(IntPtr explorer, IntPtr wParam, IntPtr lParam)
    {
        var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        var bytes = new byte[cds.cbData];
        Marshal.Copy(cds.lpData, bytes, 0, bytes.Length);
        BitConverter.GetBytes(NIM_MODIFY).CopyTo(bytes, 4);
        IntPtr data = Marshal.AllocHGlobal(bytes.Length), block = Marshal.AllocHGlobal(Marshal.SizeOf<COPYDATASTRUCT>());
        try
        {
            Marshal.Copy(bytes, 0, data, bytes.Length);
            Marshal.StructureToPtr(cds with { lpData = data }, block, false);
            return Send(explorer, WM_COPYDATA, wParam, block);
        }
        finally
        {
            Marshal.FreeHGlobal(block);
            Marshal.FreeHGlobal(data);
        }
    }

    /// <summary>
    /// Stay the Shell_TrayWnd that FindWindow returns (Explorer raises its own now and then), and match
    /// Explorer's taskbar rect so programs that measure "the taskbar" get the same answer as without us.
    /// </summary>
    private void Watch()
    {
        if (NativeMethods.FindWindow(TrayClass, null) != _tray)
            SetWindowPos(_tray, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        var explorer = Explorer();
        if (explorer == IntPtr.Zero || !NativeMethods.GetWindowRect(explorer, out var r)) return;
        if (r.Left == _mirrored.Left && r.Top == _mirrored.Top && r.Right == _mirrored.Right && r.Bottom == _mirrored.Bottom) return;
        _mirrored = r;
        SetWindowPos(_tray, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        var notify = NativeMethods.FindWindowEx(explorer, IntPtr.Zero, NotifyClass, null);
        if (_notify != IntPtr.Zero && notify != IntPtr.Zero && NativeMethods.GetWindowRect(notify, out var n))
            SetWindowPos(_notify, IntPtr.Zero, n.Left - r.Left, n.Top - r.Top, n.Width, n.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnTaskbarCreated()
    {
        // Ours too, when we asked; only a new Explorer taskbar means a restart.
        var before = _explorer;
        _explorer = IntPtr.Zero;
        if (Explorer() == before && before != IntPtr.Zero) return;
        _mirrored = default;
        Watch();
        AppPaths.Log("Tray listener: Explorer restarted");
        ExplorerRestarted?.Invoke();
        // Apps that re-added before we were back on top went straight to Explorer; ask once more.
        AskAppsToReAdd();
    }

    /// <summary>
    /// TaskbarCreated tells apps to add their icons again (Explorer sends it after a restart). Sent to every other
    /// process's windows, not broadcast, so the dock's own handler (which re-registers its appbar) stays quiet.
    /// </summary>
    private void AskAppsToReAdd()
    {
        uint self = (uint)Environment.ProcessId, msg = _taskbarCreated;
        NativeMethods.EnumWindows((h, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(h, out uint pid);
            if (pid != self) SendNotifyMessage(h, msg, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }
}
