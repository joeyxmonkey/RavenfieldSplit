using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RavenfieldSplit
{
    internal sealed class SdlControllers : IDisposable
    {
        private const uint GameControllerSubsystem = 0x00002000;
        private const uint SearchDllDirectoryAndDefaults = 0x1100;
        private static readonly Buttons[] ButtonMap =
        {
            Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.Back, 0, Buttons.Start,
            Buttons.L3, Buttons.R3, Buttons.LB, Buttons.RB, Buttons.Up, Buttons.Down, Buttons.Left, Buttons.Right
        };

        private readonly IntPtr[] handles = new IntPtr[4];
        private readonly int[] instanceIds = new int[4];
        private readonly string libraryPath;
        private readonly Action<string> warn;
        private readonly Action<string> info;
        private IntPtr module;
        private bool initialized, attempted;
        private int lastFrame = -1;

        internal SdlControllers(string libraryPath, Action<string> warn, Action<string> info = null)
        {
            this.libraryPath = libraryPath;
            this.warn = warn;
            this.info = info;
        }

        internal void Refresh(int frame)
        {
            if (frame == lastFrame) return;
            lastFrame = frame;
            if (!attempted) Initialize();
            if (!initialized) return;

            SDL_GameControllerUpdate();
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i] == IntPtr.Zero || SDL_GameControllerGetAttached(handles[i]) != 0) continue;
                SDL_GameControllerClose(handles[i]);
                handles[i] = IntPtr.Zero;
            }

            int deviceCount = SDL_NumJoysticks();
            for (int i = 0; i < deviceCount; i++)
            {
                int free = Array.IndexOf(handles, IntPtr.Zero);
                if (free < 0) break;
                if (SDL_IsGameController(i) == 0) continue;
                int instance = SDL_JoystickGetDeviceInstanceID(i);
                if (instance < 0 || HasInstance(instance)) continue;
                IntPtr handle = SDL_GameControllerOpen(i);
                if (handle == IntPtr.Zero) continue;
                handles[free] = handle;
                instanceIds[free] = instance;
            }
        }

        private bool HasInstance(int id)
        {
            for (int i = 0; i < handles.Length; i++)
                if (handles[i] != IntPtr.Zero && instanceIds[i] == id) return true;
            return false;
        }

        internal bool Read(int slot, out Pad.State state)
        {
            state = default(Pad.State);
            IntPtr controller = handles[slot - 1];
            if (controller == IntPtr.Zero) return false;
            for (int i = 0; i < ButtonMap.Length; i++)
                if (ButtonMap[i] != 0 && SDL_GameControllerGetButton(controller, i) != 0)
                    state.Buttons |= ButtonMap[i];
            state.LX = SDL_GameControllerGetAxis(controller, 0);
            state.LY = Invert(SDL_GameControllerGetAxis(controller, 1));
            state.RX = SDL_GameControllerGetAxis(controller, 2);
            state.RY = Invert(SDL_GameControllerGetAxis(controller, 3));
            state.LeftTrigger = Trigger(SDL_GameControllerGetAxis(controller, 4));
            state.RightTrigger = Trigger(SDL_GameControllerGetAxis(controller, 5));
            return true;
        }

        private static short Invert(short value) => (short)Math.Min(short.MaxValue, -(int)value);
        private static byte Trigger(short value) => (byte)(Math.Max(0, (int)value) * 255 / short.MaxValue);

        private void Initialize()
        {
            attempted = true;
            if (!File.Exists(libraryPath))
            {
                warn($"Direct gamepad input unavailable. Missing SDL2 runtime at {libraryPath}.");
                return;
            }
            module = LoadLibraryEx(libraryPath, IntPtr.Zero, SearchDllDirectoryAndDefaults);
            if (module == IntPtr.Zero)
            {
                warn($"Direct gamepad input unavailable. Windows could not load SDL2 (error {Marshal.GetLastWin32Error()}).");
                return;
            }
            try
            {
                // Bind to the absolute-path module instead of relying on Unity Mono's DLL search.
                SDL_InitSubSystem = Resolve<Init>(nameof(SDL_InitSubSystem));
                SDL_QuitSubSystem = Resolve<Quit>(nameof(SDL_QuitSubSystem));
                SDL_GetError = Resolve<Error>(nameof(SDL_GetError));
                SDL_GameControllerUpdate = Resolve<Update>(nameof(SDL_GameControllerUpdate));
                SDL_NumJoysticks = Resolve<Count>(nameof(SDL_NumJoysticks));
                SDL_IsGameController = Resolve<DeviceQuery>(nameof(SDL_IsGameController));
                SDL_JoystickGetDeviceInstanceID = Resolve<DeviceQuery>(nameof(SDL_JoystickGetDeviceInstanceID));
                SDL_GameControllerOpen = Resolve<Open>(nameof(SDL_GameControllerOpen));
                SDL_GameControllerClose = Resolve<Close>(nameof(SDL_GameControllerClose));
                SDL_GameControllerGetAttached = Resolve<Attached>(nameof(SDL_GameControllerGetAttached));
                SDL_GameControllerGetButton = Resolve<Button>(nameof(SDL_GameControllerGetButton));
                SDL_GameControllerGetAxis = Resolve<Axis>(nameof(SDL_GameControllerGetAxis));
                initialized = SDL_InitSubSystem(GameControllerSubsystem) == 0;
                if (initialized) info?.Invoke("Direct SDL2 gamepad input initialized.");
                else warn("SDL2 gamepad initialization failed. " + Marshal.PtrToStringAnsi(SDL_GetError()));
            }
            catch (EntryPointNotFoundException error) { warn(error.Message); }
            if (!initialized) Dispose();
        }

        private T Resolve<T>(string symbol) where T : Delegate
        {
            IntPtr address = GetProcAddress(module, symbol);
            if (address == IntPtr.Zero) throw new EntryPointNotFoundException($"SDL2 export {symbol} was not found.");
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        public void Dispose()
        {
            if (initialized)
            {
                foreach (IntPtr handle in handles)
                    if (handle != IntPtr.Zero) SDL_GameControllerClose(handle);
                Array.Clear(handles, 0, handles.Length);
                SDL_QuitSubSystem(GameControllerSubsystem);
                initialized = false;
            }
            if (module != IntPtr.Zero)
            {
                FreeLibrary(module);
                module = IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr module);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init(uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Quit(uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Error();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Update();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceQuery(int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Open(int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Close(IntPtr controller);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Attached(IntPtr controller);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Button(IntPtr controller, int button);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate short Axis(IntPtr controller, int axis);

        private Init SDL_InitSubSystem;
        private Quit SDL_QuitSubSystem;
        private Error SDL_GetError;
        private Update SDL_GameControllerUpdate;
        private Count SDL_NumJoysticks;
        private DeviceQuery SDL_IsGameController, SDL_JoystickGetDeviceInstanceID;
        private Open SDL_GameControllerOpen;
        private Close SDL_GameControllerClose;
        private Attached SDL_GameControllerGetAttached;
        private Button SDL_GameControllerGetButton;
        private Axis SDL_GameControllerGetAxis;
    }
}
