#if SWITCH
using System.Runtime.InteropServices;

namespace DmitryAndDemid.Rendering.Switch;

/// <summary>
/// P/Invoke surface for SDL2 + SDL2_image on the Nintendo Switch through mono-nx. Unlike the libnx platform
/// symbols (which stock mono-nx does not expose), mono-nx's <c>dl_shim_sdl2</c> exports the full SDL2 API, so
/// these resolve against <c>"__Internal"</c> the same way. SDL uses the cdecl convention and UTF-8 strings.
///
/// This backs <see cref="SdlBackend"/> — the working-video path for the Switch (SDL's 2D renderer draws every
/// sprite/target/text; fragment-shader effects are the one thing it can't do, and are stubbed). See
/// docs/switch-port.md.
/// </summary>
internal static class Sdl
{
    // mono-nx registers each native library under its real name in dl_shim.c (SDL core as "SDL2", the image
    // add-on as "SDL2_image"), NOT under "__Internal" — that name resolves only the interpreter's own symbols.
    private const string Lib = "SDL2";
    private const string Img = "SDL2_image";

    // ---- init / window / renderer ---------------------------------------------------------------------
    public const uint INIT_VIDEO = 0x00000020, INIT_AUDIO = 0x00000010, INIT_GAMECONTROLLER = 0x00002000,
                      INIT_JOYSTICK = 0x00000200;
    public const int WINDOWPOS_CENTERED = 0x2FFF0000;
    public const uint WINDOW_FULLSCREEN = 0x00000001, WINDOW_SHOWN = 0x00000004;
    public const uint RENDERER_ACCELERATED = 0x00000002, RENDERER_PRESENTVSYNC = 0x00000004,
                      RENDERER_TARGETTEXTURE = 0x00000008;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_Init(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_InitSubSystem(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Quit();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        int x, int y, int w, int h, uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GetWindowSize(IntPtr window, out int w, out int h);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyRenderer(IntPtr renderer);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetRendererOutputSize(IntPtr renderer, out int w, out int h);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderSetVSync(IntPtr renderer, int vsync);

    // ---- clear / present / draw-state -----------------------------------------------------------------
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetRenderDrawColor(IntPtr renderer, byte r, byte g, byte b, byte a);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetRenderDrawBlendMode(IntPtr renderer, int blendMode);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderClear(IntPtr renderer);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_RenderPresent(IntPtr renderer);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderFillRectF(IntPtr renderer, ref SDL_FRect rect);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderDrawLineF(IntPtr renderer, float x1, float y1, float x2, float y2);

    // ---- textures -------------------------------------------------------------------------------------
    // Memory byte order R,G,B,A on little-endian (aarch64) — matches the Stb RGBA buffers we upload.
    public const uint PIXELFORMAT_ABGR8888 = 0x16762004;
    public const int TEXTUREACCESS_STATIC = 0, TEXTUREACCESS_STREAMING = 1, TEXTUREACCESS_TARGET = 2;
    public const int BLENDMODE_NONE = 0, BLENDMODE_BLEND = 1, BLENDMODE_ADD = 2, BLENDMODE_MOD = 4, BLENDMODE_MUL = 8;
    public const int FLIP_NONE = 0, FLIP_HORIZONTAL = 1, FLIP_VERTICAL = 2;
    public const int SCALEMODE_NEAREST = 0, SCALEMODE_LINEAR = 1;

    [DllImport(Img, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr IMG_LoadTexture(IntPtr renderer, [MarshalAs(UnmanagedType.LPUTF8Str)] string file);
    [DllImport(Img, CallingConvention = CallingConvention.Cdecl)] public static extern int IMG_Init(int flags);
    // Native PNG decode to an SDL_Surface (used by the GLES backend to avoid a huge managed byte[] on the LOS).
    [DllImport(Img, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr IMG_Load([MarshalAs(UnmanagedType.LPUTF8Str)] string file);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_ConvertSurfaceFormat(IntPtr surface, uint pixelFormat, uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_FreeSurface(IntPtr surface);
    // Downscale oversized textures (the game ships 4K backgrounds; the GLES upload hangs on a 44 MB texture).
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_CreateRGBSurfaceWithFormat(uint flags, int w, int h, int depth, uint format);
    // SDL_BlitScaled is a #define for SDL_UpperBlitScaled in SDL2 — the macro name isn't an exported symbol.
    [DllImport(Lib, EntryPoint = "SDL_UpperBlitScaled", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_BlitScaled(IntPtr src, IntPtr srcrect, IntPtr dst, IntPtr dstrect);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SetSurfaceBlendMode(IntPtr surface, int blendMode);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int w, int h);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyTexture(IntPtr texture);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_QueryTexture(IntPtr texture, out uint format, out int access, out int w, out int h);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureColorMod(IntPtr texture, byte r, byte g, byte b);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureAlphaMod(IntPtr texture, byte alpha);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureBlendMode(IntPtr texture, int blendMode);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureScaleMode(IntPtr texture, int scaleMode);

    // ---- render targets -------------------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetRenderTarget(IntPtr renderer, IntPtr texture);

    // ---- copy (float variant; src can be IntPtr.Zero for the whole texture) ---------------------------
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_RenderCopyExF(IntPtr renderer, IntPtr texture, ref SDL_Rect src, ref SDL_FRect dst,
        double angle, ref SDL_FPoint center, int flip);

    // ---- events / input -------------------------------------------------------------------------------
    public const uint EVENT_QUIT = 0x100;
    public const int CONTROLLER_BUTTON_A = 0, CONTROLLER_BUTTON_B = 1, CONTROLLER_BUTTON_X = 2, CONTROLLER_BUTTON_Y = 3,
        CONTROLLER_BUTTON_BACK = 4, CONTROLLER_BUTTON_GUIDE = 5, CONTROLLER_BUTTON_START = 6,
        CONTROLLER_BUTTON_LEFTSTICK = 7, CONTROLLER_BUTTON_RIGHTSTICK = 8, CONTROLLER_BUTTON_LEFTSHOULDER = 9,
        CONTROLLER_BUTTON_RIGHTSHOULDER = 10, CONTROLLER_BUTTON_DPAD_UP = 11, CONTROLLER_BUTTON_DPAD_DOWN = 12,
        CONTROLLER_BUTTON_DPAD_LEFT = 13, CONTROLLER_BUTTON_DPAD_RIGHT = 14;
    public const int CONTROLLER_AXIS_LEFTX = 0, CONTROLLER_AXIS_LEFTY = 1, CONTROLLER_AXIS_RIGHTX = 2,
        CONTROLLER_AXIS_RIGHTY = 3, CONTROLLER_AXIS_TRIGGERLEFT = 4, CONTROLLER_AXIS_TRIGGERRIGHT = 5;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_PollEvent(out SDL_Event ev);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_PumpEvents();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int joystickIndex);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerOpen(int joystickIndex);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GameControllerClose(IntPtr gamecontroller);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GameControllerUpdate();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GameControllerGetButton(IntPtr gamecontroller, int button);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_GameControllerGetAxis(IntPtr gamecontroller, int axis);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetError();

    // ---- audio (SDL2 core) ----------------------------------------------------------------------------
    // mono-nx does NOT shim SDL2_mixer, so audio goes through SDL core: open a device and push PCM with
    // SDL_QueueAudio. Core decodes only WAV, so the Switch ships offline-converted WAVs (44100 Hz s16 stereo,
    // matching the opened device) loaded from memory via SDL_RWFromMem — no SDL file IO. SDL_AudioSpec is fully
    // blittable (primitives + IntPtr), so it marshals by pointer with no dynamic-code stub on the interpreter.
    public const ushort AUDIO_S16LSB = 0x8010;   // == AUDIO_S16SYS on little-endian (ARM)
    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_AudioSpec
    {
        public int freq; public ushort format; public byte channels; public byte silence;
        public ushort samples; public ushort padding; public uint size;
        public IntPtr callback; public IntPtr userdata;
    }
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_RWFromMem(IntPtr mem, int size);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_OpenAudioDevice(IntPtr device, int iscapture, ref SDL_AudioSpec desired, out SDL_AudioSpec obtained, int allowedChanges);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_CloseAudioDevice(uint dev);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_PauseAudioDevice(uint dev, int pauseOn);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_QueueAudio(uint dev, IntPtr data, uint len);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetQueuedAudioSize(uint dev);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_ClearQueuedAudio(uint dev);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_LoadWAV_RW(IntPtr src, int freesrc, out SDL_AudioSpec spec, out IntPtr audioBuf, out uint audioLen);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_FreeWAV(IntPtr audioBuf);

    // ---- touch (Switch touchscreen) -------------------------------------------------------------------
    // SDL surfaces the panel as a touch device; each finger's x/y are NORMALISED 0..1 in device space, so the
    // backend scales them by the drawable size to get window pixels. SDL_TouchID / SDL_FingerID are Sint64.
    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_Finger { public long id; public float x; public float y; public float pressure; }
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetNumTouchDevices();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long SDL_GetTouchDevice(int index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetNumTouchFingers(long touchId);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetTouchFinger(long touchId, int index);

    // ---- OpenGL ES context (for the shader-capable GLES backend) --------------------------------------
    public const uint WINDOW_OPENGL = 0x00000002;
    public const int GL_CONTEXT_MAJOR_VERSION = 17, GL_CONTEXT_MINOR_VERSION = 18, GL_CONTEXT_PROFILE_MASK = 21,
                     GL_DOUBLEBUFFER = 5, GL_DEPTH_SIZE = 6, GL_RED_SIZE = 0, GL_GREEN_SIZE = 1, GL_BLUE_SIZE = 2,
                     GL_ALPHA_SIZE = 3;
    public const int GL_CONTEXT_PROFILE_ES = 0x0004;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GL_SetAttribute(int attr, int value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GL_CreateContext(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GL_DeleteContext(IntPtr context);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GL_MakeCurrent(IntPtr window, IntPtr context);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GL_SwapWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GL_SetSwapInterval(int interval);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_GL_GetProcAddress([MarshalAs(UnmanagedType.LPUTF8Str)] string proc);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GL_GetDrawableSize(IntPtr window, out int w, out int h);
}

[StructLayout(LayoutKind.Sequential)]
internal struct SDL_Rect { public int x, y, w, h; }

/// <summary>The head of SDL_Surface — enough to read decoded pixels. Field order matches SDL2's struct.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SDL_Surface
{
    public uint flags;
    public IntPtr format;
    public int w;
    public int h;
    public int pitch;
    public IntPtr pixels;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SDL_FRect { public float x, y, w, h; }

[StructLayout(LayoutKind.Sequential)]
internal struct SDL_FPoint { public float x, y; }

/// <summary>SDL_Event is a 56-byte union; we only read <c>type</c> (offset 0). Oversized to be safe.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct SDL_Event { public uint type; }
#endif
