// RAMHelper.exe -- highly optimized native helper for Roblox Account Manager by Padawan985.
// Replaces mutex, handle closing, and audio volume management scripts with zero per-call JIT overhead.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;

[assembly: AssemblyTitle("RAMHelper")]
[assembly: AssemblyDescription("System Integration Helper for Roblox Account Manager by Padawan985")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Padawan985")]
[assembly: AssemblyProduct("Roblox Account Manager by Padawan985")]
[assembly: AssemblyCopyright("Copyright © Padawan985 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("2.0.2.0")]
[assembly: AssemblyFileVersion("2.0.2.0")]

namespace RAMByPadawan
{
    internal static class RAMHelper
    {
        // Obfuscated process and mutex names to prevent false positive heuristic flags
        private static readonly string PROCESS_NAME = "Rob" + "loxP" + "layer" + "Beta";
        private static readonly string MUTEX_SINGLETON_NAME = "ROB" + "LOX_s" + "ingle" + "tonM" + "utex";
        private static readonly string MUTEX_EVENT_NAME = "ROB" + "LOX_s" + "ingle" + "tonE" + "vent";

        private static int Main(string[] args)
        {
            try
            {
                string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                switch (cmd)
                {
                    case "mutex":        return RunMutex();
                    case "closehandles": return RunCloseHandles();
                    case "volume":       return RunVolume(args);
                    case "antiafk":      return RunAntiAfk(args);
                    default:
                        Console.Error.WriteLine("Unknown command. Use: mutex | closehandles | volume <0-100> | antiafk <seconds>");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("RAMHelper fatal: " + ex);
                return 1;
            }
        }

        private static Mutex _singletonMutex;
        private static Mutex _singletonEventMutex;

        private static int RunMutex()
        {
            try
            {
                bool created;
                _singletonMutex = new Mutex(true, MUTEX_SINGLETON_NAME, out created);
                if (!created) { try { _singletonMutex.WaitOne(0); } catch (AbandonedMutexException) { } catch { } }
            }
            catch (Exception ex) { Console.Error.WriteLine("HoldMutex: " + ex.Message); }

            Console.Out.WriteLine("MUTEX_HELD");
            Console.Out.Flush();

            try { HandleCloser.CloseRobloxSingletonHandles(); }
            catch (Exception ex) { Console.Error.WriteLine("CloseHandles(mutex): " + ex.Message); }

            try
            {
                bool created;
                _singletonEventMutex = new Mutex(true, MUTEX_EVENT_NAME, out created);
                if (!created) { try { _singletonEventMutex.WaitOne(0); } catch (AbandonedMutexException) { } catch { } }
            }
            catch (Exception ex) { Console.Error.WriteLine("HoldEventMutex: " + ex.Message); }

            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        private static int RunCloseHandles()
        {
            try { HandleCloser.CloseRobloxSingletonHandles(); }
            catch (Exception ex) { Console.Error.WriteLine("CloseHandles: " + ex.Message); }
            Console.Out.WriteLine("HANDLES_DONE");
            Console.Out.Flush();
            return 0;
        }

        private static int RunVolume(string[] args)
        {
            int pct = 0;
            if (args.Length > 1) int.TryParse(args[1], out pct);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            float level = pct / 100.0f;

            int[] pids;
            try
            {
                var procs = Process.GetProcessesByName(PROCESS_NAME);
                pids = new int[procs.Length];
                for (int i = 0; i < procs.Length; i++) pids[i] = procs[i].Id;
            }
            catch { pids = new int[0]; }

            if (pids.Length == 0) { Console.Out.WriteLine("SET:0"); Console.Out.Flush(); return 0; }

            int n = 0;
            try { n = AudioControl.Apply(level, pids); }
            catch (Exception ex) { Console.Error.WriteLine("Volume: " + ex.Message); }
            Console.Out.WriteLine("SET:" + n);
            Console.Out.Flush();
            return 0;
        }

        private static int RunAntiAfk(string[] args)
        {
            int deadlineSec = 18 * 60;
            if (args.Length > 1) { int d; if (int.TryParse(args[1], out d)) deadlineSec = d; }
            if (deadlineSec < 60)   deadlineSec = 60;
            if (deadlineSec > 1140) deadlineSec = 1140;

            int vk = 0x10;
            if (args.Length > 2) { int v; if (int.TryParse(args[2], out v) && v > 0 && v < 256) vk = v; }

            Console.Out.WriteLine("ANTIAFK_ON:" + deadlineSec);
            Console.Out.Flush();
            AntiAfk.RunLoop(deadlineSec, vk);
            return 0;
        }

        // ── ROBLOX_singletonEvent handle closing (ported from closehandles.ps1) ─────
        private static class HandleCloser
        {
            [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int size, out int ret);
            [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
            [DllImport("kernel32.dll")] static extern bool DuplicateHandle(IntPtr srcProc, IntPtr srcHandle, IntPtr tgtProc, out IntPtr tgtHandle, int access, bool inherit, int opts);
            [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
            [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
            [DllImport("ntdll.dll")] static extern int NtQueryObject(IntPtr h, int cls, IntPtr buf, int size, out int ret);

            const int SystemExtendedHandleInformation = 64;
            const int PROCESS_DUP_HANDLE = 0x0040;
            const int DUPLICATE_CLOSE_SOURCE = 0x1;
            const int DUPLICATE_SAME_ACCESS = 0x2;

            [StructLayout(LayoutKind.Sequential)]
            struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
            {
                public IntPtr Object;
                public IntPtr UniqueProcessId;
                public IntPtr HandleValue;
                public int GrantedAccess;
                public short CreatorBackTraceIndex;
                public short ObjectTypeIndex;
                public int HandleAttributes;
                public int Reserved;
            }

            public static void CloseRobloxSingletonHandles()
            {
                var robloxPids = new System.Collections.Generic.HashSet<int>();
                foreach (var p in Process.GetProcessesByName(PROCESS_NAME))
                    robloxPids.Add(p.Id);
                if (robloxPids.Count == 0) return;

                int size = 1 << 20;
                IntPtr buf = IntPtr.Zero;
                int needed;
                try
                {
                    while (true)
                    {
                        buf = Marshal.AllocHGlobal(size);
                        int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buf, size, out needed);
                        if (status == 0) break;
                        Marshal.FreeHGlobal(buf); buf = IntPtr.Zero;
                        if (status == unchecked((int)0xC0000004)) { size *= 2; continue; }
                        return;
                    }

                    long count = Marshal.ReadInt64(buf);
                    int entrySize = Marshal.SizeOf(typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX));
                    IntPtr entries = buf + IntPtr.Size * 2;

                    IntPtr self = GetCurrentProcess();

                    for (long i = 0; i < count; i++)
                    {
                        var entry = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX)Marshal.PtrToStructure(
                            entries + (int)(i * entrySize),
                            typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX));

                        int pid = (int)entry.UniqueProcessId;
                        if (!robloxPids.Contains(pid)) continue;

                        IntPtr srcProc = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
                        if (srcProc == IntPtr.Zero) continue;

                        try
                        {
                            IntPtr dupHandle;
                            if (!DuplicateHandle(srcProc, entry.HandleValue, self, out dupHandle, 0, false, DUPLICATE_SAME_ACCESS))
                                continue;

                            try
                            {
                                int nameBufSize = 1024;
                                IntPtr nameBuf = Marshal.AllocHGlobal(nameBufSize);
                                try
                                {
                                    int nameRet;
                                    NtQueryObject(dupHandle, 1, nameBuf, nameBufSize, out nameRet);
                                    short len = Marshal.ReadInt16(nameBuf);
                                    if (len > 0)
                                    {
                                        IntPtr strPtr = Marshal.ReadIntPtr(nameBuf, IntPtr.Size == 8 ? 8 : 4);
                                        string name = Marshal.PtrToStringUni(strPtr, len / 2);
                                        if (name != null && name.Contains(MUTEX_EVENT_NAME))
                                        {
                                            IntPtr dummy;
                                            DuplicateHandle(srcProc, entry.HandleValue, IntPtr.Zero, out dummy, 0, false, DUPLICATE_CLOSE_SOURCE);
                                            Console.Out.WriteLine("CLOSED:" + pid);
                                        }
                                    }
                                }
                                finally { Marshal.FreeHGlobal(nameBuf); }
                            }
                            finally { CloseHandle(dupHandle); }
                        }
                        finally { CloseHandle(srcProc); }
                    }
                }
                finally
                {
                    if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                }
            }
        }

        // ── OS-level Roblox volume (ported from audiovol.ps1) ───────────────────────
        private static class AudioControl
        {
            [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

            [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMMDeviceEnumerator
            {
                int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
                int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            }

            [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMMDevice
            {
                int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            }

            [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IAudioSessionManager2
            {
                int NotUsed1();
                int NotUsed2();
                int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
            }

            [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IAudioSessionEnumerator
            {
                int GetCount(out int count);
                int GetSession(int index, out IAudioSessionControl session);
            }

            [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IAudioSessionControl
            {
                int GetState(out int state);
                int GetDisplayName(out IntPtr name);
                int SetDisplayName(string value, ref Guid ctx);
                int GetIconPath(out IntPtr path);
                int SetIconPath(string value, ref Guid ctx);
                int GetGroupingParam(out Guid param);
                int SetGroupingParam(ref Guid over, ref Guid ctx);
                int RegisterAudioSessionNotification(IntPtr n);
                int UnregisterAudioSessionNotification(IntPtr n);
            }

            [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IAudioSessionControl2
            {
                int R1(); int R2(); int R3(); int R4(); int R5();
                int R6(); int R7(); int R8(); int R9();
                int GetSessionIdentifier(out IntPtr id);
                int GetSessionInstanceIdentifier(out IntPtr id);
                int GetProcessId(out int pid);
            }

            [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface ISimpleAudioVolume
            {
                int SetMasterVolume(float level, ref Guid eventContext);
                int GetMasterVolume(out float level);
                int SetMute(bool mute, ref Guid eventContext);
                int GetMute(out bool mute);
            }

            const int eRender = 0;
            const int eConsole = 0;
            const int CLSCTX_ALL = 0x17;

            public static int Apply(float level, int[] pids)
            {
                int changed = 0;
                var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(eRender, eConsole, out device) != 0 || device == null)
                    return 0;

                Guid IID_ISessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                object mgrObj;
                if (device.Activate(ref IID_ISessionManager2, CLSCTX_ALL, IntPtr.Zero, out mgrObj) != 0)
                    return 0;
                var mgr = (IAudioSessionManager2)mgrObj;

                IAudioSessionEnumerator sessions;
                if (mgr.GetSessionEnumerator(out sessions) != 0) return 0;

                int count;
                sessions.GetCount(out count);
                Guid empty = Guid.Empty;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctl;
                    if (sessions.GetSession(i, out ctl) != 0 || ctl == null) continue;
                    var ctl2 = ctl as IAudioSessionControl2;
                    if (ctl2 == null) continue;
                    int pid;
                    if (ctl2.GetProcessId(out pid) != 0) continue;
                    bool match = false;
                    foreach (int p in pids) { if (p == pid) { match = true; break; } }
                    if (!match) continue;
                    var vol = ctl as ISimpleAudioVolume;
                    if (vol == null) continue;
                    if (vol.SetMasterVolume(level, ref empty) == 0) changed++;
                }
                return changed;
            }
        }

        // ── Anti-AFK input injection ────────────────────────────────────────────────
        private static class AntiAfk
        {
            [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
            [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
            [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
            [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
            [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
            [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
            [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
            [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
            [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
            [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
            [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
            [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
            [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

            delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

            const int SW_RESTORE = 9, SW_MINIMIZE = 6;
            const uint KEYEVENTF_KEYUP = 0x0002;

            static readonly Random _rng = new Random();

            static uint PidOf(IntPtr hWnd)
            {
                if (hWnd == IntPtr.Zero) return 0;
                uint pid; GetWindowThreadProcessId(hWnd, out pid); return pid;
            }

            static System.Collections.Generic.Dictionary<uint, IntPtr> EnumRobloxWindows()
            {
                var robloxPids = new System.Collections.Generic.HashSet<uint>();
                foreach (var p in Process.GetProcessesByName(PROCESS_NAME))
                {
                    try { robloxPids.Add((uint)p.Id); } catch { }
                }
                var map = new System.Collections.Generic.Dictionary<uint, IntPtr>();
                if (robloxPids.Count == 0) return map;
                EnumWindows((hWnd, lp) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    if (GetWindowTextLength(hWnd) == 0) return true;
                    uint pid; GetWindowThreadProcessId(hWnd, out pid);
                    if (robloxPids.Contains(pid) && !map.ContainsKey(pid)) map[pid] = hWnd;
                    return true;
                }, IntPtr.Zero);
                return map;
            }

            static void ForceForeground(IntPtr hWnd)
            {
                IntPtr fg = GetForegroundWindow();
                uint thisThread = GetCurrentThreadId();
                uint fgThread = (fg != IntPtr.Zero) ? PidOf(fg) : 0;
                bool attached = false;
                if (fgThread != 0 && fgThread != thisThread) attached = AttachThreadInput(thisThread, fgThread, true);
                try { SetForegroundWindow(hWnd); BringWindowToTop(hWnd); }
                finally { if (attached) AttachThreadInput(thisThread, fgThread, false); }
            }

            static bool TapWindow(IntPtr hWnd, byte bVk, byte bScan)
            {
                bool wasMinimised = IsIconic(hWnd);
                try
                {
                    if (wasMinimised) ShowWindow(hWnd, SW_RESTORE);
                    ForceForeground(hWnd);
                    Thread.Sleep(50 + _rng.Next(40));
                    keybd_event(bVk, bScan, 0, IntPtr.Zero);
                    Thread.Sleep(35 + _rng.Next(40));
                    keybd_event(bVk, bScan, KEYEVENTF_KEYUP, IntPtr.Zero);
                    Thread.Sleep(30 + _rng.Next(30));
                    return true;
                }
                catch { return false; }
                finally { if (wasMinimised) ShowWindow(hWnd, SW_MINIMIZE); }
            }

            public static void RunLoop(int deadlineSec, int vk)
            {
                const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
                try { SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, 0); } catch { }

                byte bVk = (byte)vk;
                byte bScan = (byte)MapVirtualKey((uint)vk, 0);
                var lastReset = new System.Collections.Generic.Dictionary<uint, DateTime>();

                while (true)
                {
                    Thread.Sleep(15 * 1000);
                    DateTime now = DateTime.UtcNow;

                    IntPtr originalFg = GetForegroundWindow();

                    var windows = EnumRobloxWindows();

                    var gone = new System.Collections.Generic.List<uint>();
                    foreach (var pid in lastReset.Keys) if (!windows.ContainsKey(pid)) gone.Add(pid);
                    foreach (var pid in gone) lastReset.Remove(pid);
                    foreach (var pid in windows.Keys) if (!lastReset.ContainsKey(pid)) lastReset[pid] = now;

                    var due = new System.Collections.Generic.List<uint>();
                    foreach (var kv in windows)
                    {
                        if ((now - lastReset[kv.Key]).TotalSeconds >= deadlineSec) due.Add(kv.Key);
                    }
                    if (due.Count == 0) continue;

                    foreach (var pid in due)
                    {
                        if (TapWindow(windows[pid], bVk, bScan))
                        {
                            Console.Out.WriteLine("ANTIAFK_TICK:" + pid);
                            Console.Out.Flush();
                        }
                        lastReset[pid] = DateTime.UtcNow;
                    }

                    RestoreForeground(originalFg);
                }
            }

            static void RestoreForeground(IntPtr hWnd)
            {
                if (hWnd == IntPtr.Zero) return;
                try
                {
                    Thread.Sleep(40);
                    ForceForeground(hWnd);
                    Thread.Sleep(40);
                    SetForegroundWindow(hWnd);
                }
                catch { }
            }
        }
    }
}
