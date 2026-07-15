using System.Numerics;
using System.Runtime.InteropServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Opengl;
using Android.OS;
using Android.Views;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using Javax.Microedition.Khronos.Egl;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using AndroidPlatform = DmitryAndDemid.Utils.Platform;

// Debuggable so startup can be inspected (run-as, stdout in logcat) while the port is being brought up. The
// launcher icon and label are set here at the application level so the drawer picks them up.
[assembly: Application(Debuggable = true, Icon = "@mipmap/icon", Label = "AAG2 ~ Subhumanian Fartalism")]

namespace DmitryAndDemid.Android;

/// <summary>
/// The whole Android host. It owns the GL ES surface and the frame loop; the game itself is untouched —
/// <see cref="Runtime.StartAndroid"/> attaches the Silk/OpenGL backend to the context created here, and
/// <see cref="Runtime.RunFrame"/> is called once per <c>onDrawFrame</c>.
///
/// Landscape and fullscreen, because the game renders a fixed 4:3 playfield and letterboxes it.
/// </summary>
[Activity(
    Label = "AAG2 ~ Subhumanian Fartalism",
    Icon = "@mipmap/icon",
    MainLauncher = true,
    Name = "co.sugar.aag2.MainActivity",   // stable Java name so the app shortcut can target it
    Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    // One instance only: a relaunch (icon tap, app shortcut) resumes it via OnNewIntent instead of building a
    // second Activity, which would try to initialise the process-global renderer twice.
    LaunchMode = LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
// The long-press launcher menu offers "Configure", which reopens the game straight into its settings screen —
// the closest thing to the desktop configurator, which is GTK and cannot run here.
[MetaData("android.app.shortcuts", Resource = "@xml/shortcuts")]
public class MainActivity : Activity
{
    private GLSurfaceView View = null!;
    private GameRenderer Renderer = null!;
    private BackCallback? BackHandler;   // held so the Java dispatcher's callback is not GC'd

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Everything the game writes (config, save, replays) goes to the app's private storage; nothing inside
        // an APK is writable. Reads of the packaged content run through AndroidAssetSource, built on the GL
        // thread (see GameRenderer) because unpacking the assets is far too slow to do on the UI thread.
        string storage = FilesDir?.AbsolutePath ?? CacheDir!.AbsolutePath;
        AndroidPlatform.DataDirectory = storage;
        AndroidPlatform.FatalErrorHandler = message => global::Android.Util.Log.Error("aag2", message);

        // Launched from the "Configure" app shortcut: open the game directly on its settings screen.
        if (Intent?.Action == "co.sugar.aag2.CONFIGURE")
            Runtime.OpenSettingsOnStart = true;

        global::Android.Util.Log.Info("aag2", "OnCreate: building GLSurfaceView");
        Renderer = new GameRenderer(base.Assets!, storage);

        View = new GLSurfaceView(this);
        View.SetEGLContextClientVersion(3);          // GL ES 3.0: what Assets/Shaders/gles targets
        View.SetEGLConfigChooser(8, 8, 8, 8, 16, 0);
        // Keep the GL context (and every texture/shader/target already uploaded) across pause/resume. Without
        // this the context is destroyed on background and the game comes back to a blank screen, because our
        // OnSurfaceChanged deliberately does not re-run startup once the game exists.
        View.PreserveEGLContextOnPause = true;
        View.SetRenderer(Renderer);
        View.RenderMode = Rendermode.Continuously;

        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        SetContentView(View);
        EnterImmersive();

        // Android 13+ delivers back through this dispatcher (and on 15+ targets OnBackPressed is not called at
        // all). Registering here makes the back gesture and button act as Escape instead of sending the app home.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            BackHandler = new BackCallback(OnBackInvoked);
            OnBackInvokedDispatcher!.RegisterOnBackInvokedCallback(
                0 /* OnBackInvokedDispatcher.PRIORITY_DEFAULT */, BackHandler);
        }
    }

    /// <summary>
    /// Hides the status bar and the navigation buttons/gesture pill, and keeps them hidden: a swipe reveals
    /// them briefly, then they slide away again (sticky immersive). The game owns the whole screen.
    /// </summary>
    private void EnterImmersive()
    {
        if (Window == null)
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            Window.SetDecorFitsSystemWindows(false);
            var controller = Window.InsetsController;
            if (controller != null)
            {
                controller.Hide(WindowInsets.Type.SystemBars());
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }
        }

        // Also drive the legacy decor-view flags. On its own the InsetsController call was not hiding the bars
        // on this device; the flags on the DECOR view (not the GL surface, which was the earlier mistake) are
        // what actually clears them, and they remain honoured alongside the controller.
#pragma warning disable CA1422
        Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
            SystemUiFlags.ImmersiveSticky | SystemUiFlags.HideNavigation |
            SystemUiFlags.Fullscreen | SystemUiFlags.LayoutStable |
            SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen);
#pragma warning restore CA1422
    }

    // SingleTask: a relaunch (icon or the "Configure" shortcut) arrives here instead of OnCreate. If the game
    // is already up, open its settings screen directly; otherwise flag it for when startup finishes.
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.Action != "co.sugar.aag2.CONFIGURE")
            return;
        if (Runtime.CurrentRuntime != null)
            Runtime.CurrentRuntime.AddScreen(new DmitryAndDemid.Screens.SettingsScreen());
        else
            Runtime.OpenSettingsOnStart = true;
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        // The system shows the bars whenever focus returns (after a notification shade, a swipe, etc.); hide
        // them again so they do not linger.
        if (hasFocus)
            EnterImmersive();
    }

    protected override void OnPause()
    {
        base.OnPause();
        View.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        View.OnResume();
        EnterImmersive();
    }

    /// <summary>
    /// Every finger currently down, in surface pixels, handed to the backend. The game's TouchControls reads
    /// them back through Engine.Input, exactly as it does from the mouse on desktop.
    /// </summary>
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null)
            return false;

        bool released = e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel;
        Vector2[] touches = released
            ? []
            : Enumerable.Range(0, e.PointerCount)
                .Select(i => new Vector2(e.GetX(i), e.GetY(i)))
                .ToArray();

        Renderer.Touches = touches;
        return true;
    }

    // Hardware keyboard: forward key presses/releases to the renderer, which hands them to the backend's key
    // state so the game reads them exactly as on desktop.
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Back)
        {
            // On 13+ the registered OnBackInvokedDispatcher already delivers this as one Escape; calling it
            // here too would fire twice (on the main menu that is "focus exit" then "activate exit" = quit).
            // Only the pre-13 path, which has no dispatcher, invokes it here. Either way the key is consumed
            // so the Activity is not finished.
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                OnBackInvoked();
            return true;
        }
        if (Renderer.SetKey(keyCode, true))
            return true;
        return base.OnKeyDown(keyCode, e);
    }

    // Back is Escape. On Android 13+ the system routes back through the OnBackInvokedDispatcher (registered in
    // OnCreate), and OnBackPressed is no longer called on newer targets — so intercepting the hardware BACK
    // key here as well covers physical keys and older devices. Either way the game gets one Escape and the app
    // is not sent home.
    private void OnBackInvoked()
    {
        global::Android.Util.Log.Info("aag2", "back -> Escape");
        Renderer.PressBack();
    }

    /// <summary>Adapts the game's back handler to Android 13+'s predictive-back dispatcher.</summary>
    private sealed class BackCallback(Action onBack) : Java.Lang.Object, global::Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => onBack();
    }

    public override bool OnKeyUp(Keycode keyCode, KeyEvent? e)
    {
        // Consume BACK's key-up too: handled on key-down, and letting it reach base would finish the Activity
        // (send the app home) despite the down being consumed.
        if (keyCode == Keycode.Back)
            return true;
        if (Renderer.SetKey(keyCode, false))
            return true;
        return base.OnKeyUp(keyCode, e);
    }
}

/// <summary>
/// Drives the game from the GL thread. onSurfaceCreated is the first moment a GL context exists, which is why
/// the game is started there rather than in OnCreate.
/// </summary>
public class GameRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
{
    private readonly AssetManager Assets;
    private readonly string Storage;

    private Runtime? Game;
    private SilkGLBackend? Backend;
    private bool Failed;

    /// <summary>Written from the UI thread, read from the GL thread; a torn read costs at most one frame.</summary>
    public volatile Vector2[] Touches = [];

    public GameRenderer(AssetManager assets, string storage)
    {
        Assets = assets;
        Storage = storage;
    }

    public void OnSurfaceCreated(Javax.Microedition.Khronos.Opengles.IGL10? gl, Javax.Microedition.Khronos.Egl.EGLConfig? config)
    {
        global::Android.Util.Log.Info("aag2", "OnSurfaceCreated");
        // Nothing to do yet: the surface has no size until OnSurfaceChanged, and the game needs one.
    }

    private bool DrawLogged;

    public void OnSurfaceChanged(Javax.Microedition.Khronos.Opengles.IGL10? gl, int width, int height)
    {
        global::Android.Util.Log.Info("aag2", $"OnSurfaceChanged {width}x{height}");
        if (Game != null || Failed)
            return;

        // The process outlived a previous Activity instance (e.g. a relaunch that recreated the Activity while
        // the process stayed alive): the game and its GL context are still here, so reuse them rather than
        // initialising a second backend — which is exactly what threw "Backend already set".
        if (Engine.IsInitialized && Runtime.CurrentRuntime != null)
        {
            global::Android.Util.Log.Info("aag2", "reusing existing runtime after Activity restart");
            Game = Runtime.CurrentRuntime;
            Backend = Engine.Backend as SilkGLBackend;
            return;
        }

        try
        {
            // First thing on the GL thread: unpack the assets (slow, one-time) and point the game at them.
            // Doing it here rather than in OnCreate keeps the 100+ MB copy off the UI thread.
            global::Android.Util.Log.Info("aag2", $"surface {width}x{height}; unpacking assets…");
            DmitryAndDemid.Utils.Assets.Source = new AndroidAssetSource(Assets, Storage);
            global::Android.Util.Log.Info("aag2", "assets unpacked; creating GL API…");

            GL api = GL.GetApi(new LamdaNativeContext(GetProcAddress));
            global::Android.Util.Log.Info("aag2", $"GL context: {api.GetStringS(Silk.NET.OpenGL.StringName.Version)}");
            Runtime runtime = new();
            Runtime.CurrentRuntime = runtime;

            // StartAndroid loads every asset before it returns; on the GL thread that is exactly what we want,
            // because every one of those loads needs the context that only exists here.
            runtime.StartAndroid(api, width, height, new AndroidAudio()).GetAwaiter().GetResult();

            Backend = Engine.Backend as SilkGLBackend;
            Game = runtime;
            global::Android.Util.Log.Info("aag2", "startup complete");
        }
        catch (Exception exception)
        {
            Failed = true;
            global::Android.Util.Log.Error("aag2", $"startup failed: {exception}");
        }
    }

    public void OnDrawFrame(Javax.Microedition.Khronos.Opengles.IGL10? gl)
    {
        if (!DrawLogged)
        {
            global::Android.Util.Log.Info("aag2", $"OnDrawFrame (game={(Game != null)}, failed={Failed})");
            DrawLogged = true;
        }
        if (Game == null)
            return;

        Backend?.SetTouches(Touches);
        Game.RunFrame();
    }

    /// <summary>
    /// A single Escape press, for the Android back button/gesture: held briefly so the game reads one press,
    /// then released. Posting the release to the main looper keeps both edges on the same (UI) thread.
    /// </summary>
    public void PressBack()
    {
        Backend?.SetKeyState(KeyCode.Escape, true);
        new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!).PostDelayed(
            () => Backend?.SetKeyState(KeyCode.Escape, false), 160);
    }

    /// <summary>Forwards a hardware key to the backend. Returns true if the key is one the game uses.</summary>
    public bool SetKey(Keycode keyCode, bool pressed)
    {
        if (Backend == null || MapKey(keyCode) is not { } key)
            return false;
        Backend.SetKeyState(key, pressed);
        return true;
    }

    /// <summary>Android key codes to the game's KeyCode, for the keys the game actually reads.</summary>
    private static KeyCode? MapKey(Keycode code) => code switch
    {
        Keycode.DpadLeft => KeyCode.Left,
        Keycode.DpadRight => KeyCode.Right,
        Keycode.DpadUp => KeyCode.Up,
        Keycode.DpadDown => KeyCode.Down,
        Keycode.Enter or Keycode.NumpadEnter or Keycode.DpadCenter => KeyCode.Enter,
        Keycode.Escape => KeyCode.Escape,
        Keycode.ShiftLeft => KeyCode.LeftShift,
        Keycode.ShiftRight => KeyCode.RightShift,
        Keycode.Space => KeyCode.Space,
        Keycode.Tab => KeyCode.Tab,
        Keycode.Z => KeyCode.Z,
        Keycode.X => KeyCode.X,
        Keycode.C => KeyCode.C,
        Keycode.B => KeyCode.B,
        Keycode.P => KeyCode.P,
        _ => null,
    };

    /// <summary>
    /// Where Silk gets its GL entry points. eglGetProcAddress is the portable answer, but a good few Android
    /// drivers return null from it for the *core* ES functions and only resolve extensions, so the ES library
    /// itself is asked first through dlsym.
    /// </summary>
    private static nint GetProcAddress(string name)
    {
        foreach (string library in new[] { "libGLESv3.so", "libGLESv2.so" })
        {
            nint handle = dlopen(library, RTLD_LAZY);
            if (handle == 0)
                continue;
            nint symbol = dlsym(handle, name);
            if (symbol != 0)
                return symbol;
        }
        return eglGetProcAddress(name);
    }

    private const int RTLD_LAZY = 1;

    [DllImport("libdl.so")] private static extern nint dlopen(string path, int flags);
    [DllImport("libdl.so")] private static extern nint dlsym(nint handle, string symbol);
    [DllImport("libEGL.so")] private static extern nint eglGetProcAddress(string name);
}
