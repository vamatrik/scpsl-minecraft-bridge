using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.DirectX.Direct3D11;

namespace SlBridge.Compositor
{
    /// <summary>
    /// Компоновщик: прозрачное для мыши окно поверх SCP:SL. Берёт кадр SL, а там, где SL видит
    /// пурпурный «фон-ключ», подставляет кадр Minecraft. Ввод идёт прямо в SL.
    /// Горячие клавиши: Ctrl+Shift+F12 — вкл/выкл, Ctrl+Shift+F11 — показать маску ключа.
    /// </summary>
    internal static unsafe class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Params
        {
            public float KR, KG, KB, KeyThreshold, KeySoftness, KeyMinBrightness, ShowMask, HasMc;
            public float SlOx, SlOy, SlSx, SlSy, McOx, McOy, McSx, McSy;
            public float Adaptive, CurX, CurY, CurScale;
            public float KeyRel, Pad1, Pad2, Pad3;
        }

        /// <summary>
        /// Захват окна включает рамку и заголовок. Считаем, где внутри кадра клиентская часть,
        /// чтобы брать только её (иначе картинка Minecraft сдвинута, а снизу чёрная полоса).
        /// </summary>
        private static void ClientUv(WindowCapture cap, out float ox, out float oy, out float sx, out float sy)
        {
            ox = 0; oy = 0; sx = 1; sy = 1;
            if (cap == null || cap.Width <= 0 || cap.Height <= 0) return;
            var client = Native.ClientScreenRect(cap.Hwnd);
            if (Native.DwmGetWindowAttribute(cap.Hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT frame, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0)
                Native.GetWindowRect(cap.Hwnd, out frame);
            ox = Math.Max(0, client.Left - frame.Left) / (float)cap.Width;
            oy = Math.Max(0, client.Top - frame.Top) / (float)cap.Height;
            sx = Math.Min(1f - ox, client.W / (float)cap.Width);
            sy = Math.Min(1f - oy, client.H / (float)cap.Height);
            if (sx <= 0 || sy <= 0) { ox = 0; oy = 0; sx = 1; sy = 1; }
        }

        private static Settings _s;
        private static IntPtr _hwnd;
        private static bool _enabled = true, _showMask;
        private static Native.WndProc _wndProc; // держим ссылку, чтобы GC не собрал делегат

        private static ID3D11Device _device;
        private static ID3D11DeviceContext _ctx;
        private static IDirect3DDevice _rtDevice;
        private static IDXGISwapChain1 _swap;
        private static ID3D11RenderTargetView _rtv;
        private static int _swapW, _swapH;
        private static int _fpsSl, _fpsMc, _fpsOut;
        private static ID3D11PixelShader _refPs;
        private static ID3D11Texture2D _refTex;
        private static ID3D11RenderTargetView _refRtv;
        private static ID3D11ShaderResourceView _refSrv;
        private static long _fpsAt;
        private static double _renderMs;
        private static Func<Native.RECT> _slRectRef;

        // Окно для OBS: обычное окно (не оверлей) с той же картинкой. Оверлей сделан через DirectComposition —
        // OBS его снимает ненадёжно (кадр и чёрный экран). Это окно стоит прямо под окном SL, его не видно.
        private static IntPtr _obsHwnd;
        private static IDXGISwapChain1 _obsSwap;
        private static ID3D11RenderTargetView _obsRtv;
        private static int _obsW, _obsH;
        private static IDCompositionDevice _dcomp;
        private static IDCompositionTarget _target;
        private static IDCompositionVisual _visual;
        private static ID3D11VertexShader _vs;
        private static ID3D11PixelShader _ps;
        private static ID3D11SamplerState _sampler;
        private static ID3D11Buffer _cb;

        private static WindowCapture _sl, _mc;

        private static System.IO.StreamWriter _log;

        private static ID3D11Texture2D _refStage;
        private static bool _presentErrLogged;
        private static int _diagLeft = 3;
        private static bool? _exclusive;

        /// <summary>
        /// SL в эксклюзивном полноэкранном режиме — Windows не показывает поверх него ни одного окна, оверлей тоже.
        /// Пишем в лог и кладём файл-флаг: лаунчер покажет подсказку «поставь в SL режим без рамки».
        /// </summary>
        private static void CheckExclusive()
        {
            try
            {
                bool ex = _sl != null && Native.GetForegroundWindow() == _sl.Hwnd
                          && Native.SHQueryUserNotificationState(out int st) == 0 && st == 3;
                if (_exclusive == ex) return;
                _exclusive = ex;
                string flag = System.IO.Path.Combine(AppContext.BaseDirectory, "exclusive.flag");
                if (ex)
                {
                    Log("SCP:SL в ЭКСКЛЮЗИВНОМ полноэкранном режиме — оверлей поверх него не виден. В настройках SL: режим окна «Без рамки» (Borderless).");
                    System.IO.File.WriteAllText(flag, DateTime.Now.ToString("O"));
                }
                else if (System.IO.File.Exists(flag)) System.IO.File.Delete(flag);
            }
            catch { }
        }

        /// <summary>Диагностика ключа: какой «фон сейчас» видит компоновщик и что ключ даёт для него самого.</summary>
        private static void DiagRef()
        {
            try
            {
                if (_refTex == null || _ctx == null) return;
                _refStage ??= _device.CreateTexture2D(new Texture2DDescription
                {
                    Width = 1, Height = 1, MipLevels = 1, ArraySize = 1, Format = Format.R16G16B16A16_Float,
                    SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
                });
                _ctx.CopyResource(_refStage, _refTex);
                var m = _ctx.Map(_refStage, 0, MapMode.Read);
                ushort* h = (ushort*)m.DataPointer;
                float r = (float)BitConverter.UInt16BitsToHalf(h[0]), g = (float)BitConverter.UInt16BitsToHalf(h[1]), b = (float)BitConverter.UInt16BitsToHalf(h[2]), n = (float)BitConverter.UInt16BitsToHalf(h[3]);
                _ctx.Unmap(_refStage, 0);
                float other = Math.Max(r, b), dom = (g - other) / Math.Max(g, 1e-3f);
                float k = Math.Clamp((dom - _s.KeyThreshold) / Math.Max(_s.KeySoftness, 1e-3f), 0, 1) * Math.Clamp((g - _s.KeyMinBrightness) / 0.04f, 0, 1);
                bool vis = Native.IsWindowVisible(_hwnd);
                Native.DwmGetWindowAttribute(_hwnd, 14 /*DWMWA_CLOAKED*/, out int cloaked, 4);
                Native.GetWindowRect(_hwnd, out var wr);
                // окна выше оверлея (по z-порядку), видимые и пересекающие его
                var above = new System.Collections.Generic.List<string>();
                for (IntPtr w = Native.GetWindow(_hwnd, 3 /*GW_HWNDPREV*/); w != IntPtr.Zero && above.Count < 6; w = Native.GetWindow(w, 3))
                {
                    if (!Native.IsWindowVisible(w)) continue;
                    Native.GetWindowRect(w, out var r2);
                    if (r2.Right <= wr.Left || r2.Left >= wr.Right || r2.Bottom <= wr.Top || r2.Top >= wr.Bottom) continue;
                    var t = new System.Text.StringBuilder(128); Native.GetWindowText(w, t, 128);
                    var c = new System.Text.StringBuilder(128); Native.GetClassName(w, c, 128);
                    Native.GetWindowThreadProcessId(w, out uint pid);
                    string pn = ""; try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
                    above.Add($"{pn}:{c}:«{t}» {r2.Left},{r2.Top} {r2.W}x{r2.H}");
                }
                Log($"Окно оверлея: {wr.Left},{wr.Top} {wr.W}x{wr.H}, скрыто DWM: {cloaked}, стиль {Native.GetWindowLong(_hwnd, -20):X}, выше него: {(above.Count == 0 ? "никого" : string.Join(" | ", above))}");
                Log($"Ключ: фон сейчас ({r * 255:0},{g * 255:0},{b * 255:0}), согласны {n:0}/12, ключ фона {k:0.00}, отсечка {_s.KeyRelBrightness:0.00}, оверлей {(vis ? "виден" : "СКРЫТ")}{(_enabled ? "" : ", ВЫКЛЮЧЕН (Ctrl+Shift+F12)")}{(_showMask ? ", маска" : "")}");
            }
            catch (Exception e) { Log("Диагностика ключа: " + e.Message); }
        }

        public static void Log(string msg)
        {
            try { _log?.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}"); } catch { }
        }

        [STAThread]
        private static void Main()
        {
            try
            {
                _log = new System.IO.StreamWriter(System.IO.Path.Combine(AppContext.BaseDirectory, "compositor.log"), false) { AutoFlush = true };
            }
            catch { }
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("ПАДЕНИЕ: " + e.ExceptionObject);
            Log("Старт оверлея");
            _s = Settings.Load();
            _showMask = _s.DebugShowMask;
            CreateWindow();
            InitD3D();

            Native.RegisterHotKey(_hwnd, 1, Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT, 0x7B); // F12
            Native.RegisterHotKey(_hwnd, 2, Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT, 0x7A); // F11

            var sw = Stopwatch.StartNew();
            long lastLayout = -10000;
            Native.RECT slRect = default;
            _slRectRef = () => slRect;
            bool visible = false;

            while (true)
            {
                while (Native.PeekMessage(out var msg, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
                {
                    if (msg.message == Native.WM_QUIT) return;
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }

                // Раз в 150 мс ищем окна и подгоняем раскладку.
                if (sw.ElapsedMilliseconds - lastLayout > 150)
                {
                    lastLayout = sw.ElapsedMilliseconds;
                    FindWindows();
                    UpdateUiMode();
                    // Окно Minecraft закрыто (Esc/E), а фокус остался у Minecraft — Windows иногда не отдаёт фокус
                    // с первого раза. Тогда ввод уходит в никуда и всё «зависает». Повторяем, пока фокус не вернётся в SL.
                    if (!_mcUi && _sl != null && _mc != null && Native.GetForegroundWindow() == _mc.Hwnd)
                    {
                        Log("Фокус остался у Minecraft — возвращаю в SL");
                        Native.ForceForeground(_sl.Hwnd);
                    }
                    // И наоборот: окно Minecraft открыто, а фокус у SL — отдаём Minecraft, иначе мышь не работает.
                    else if (_mcUi && _sl != null && _mc != null && Native.GetForegroundWindow() == _sl.Hwnd)
                        Native.ForceForeground(_mc.Hwnd);
                    bool ok = _enabled && _sl != null && !Native.IsIconic(_sl.Hwnd);
                    if (ok)
                    {
                        slRect = Native.ClientScreenRect(_sl.Hwnd);
                        ok = slRect.W > 16 && slRect.H > 16;
                    }
                    if (_mcUi && _sl != null && _mc != null) FitMinecraft(Native.ClientScreenRect(_sl.Hwnd));
                    if (ok)
                    {
                        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, slRect.Left, slRect.Top, slRect.W, slRect.H, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                        if (_mc != null && _s.ResizeMinecraft) FitMinecraft(slRect);
                        ResizeSwap(slRect.W, slRect.H);
                        if (_obsHwnd != IntPtr.Zero)
                        {
                            // сразу под окном SL: картинка есть, а на экране его не видно
                            Native.SetWindowPos(_obsHwnd, _sl.Hwnd, slRect.Left, slRect.Top, slRect.W, slRect.H, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                            ResizeObs(slRect.W, slRect.H);
                        }
                    }
                    if (ok != visible)
                    {
                        Native.ShowWindow(_hwnd, ok ? 4 /*SW_SHOWNOACTIVATE*/ : 0 /*SW_HIDE*/);
                        if (_obsHwnd != IntPtr.Zero) Native.ShowWindow(_obsHwnd, ok ? 4 : 0);
                        visible = ok;
                    }
                }

                if (!visible || _sl == null) { Thread.Sleep(20); continue; }

                bool newSl = _sl.Poll(_ctx);
                bool newMc = _mc != null && _mc.Poll(_ctx);
                if (newSl) _fpsSl++;
                if (newMc) _fpsMc++;
                // замер: раз в 10 с — сколько кадров реально пришло от SL и Minecraft и сколько выдал оверлей
                if (sw.ElapsedMilliseconds - _fpsAt >= 10000)
                {
                    double sec = (sw.ElapsedMilliseconds - _fpsAt) / 1000.0;
                    Log($"FPS: SL {_fpsSl / sec:0}, Minecraft {_fpsMc / sec:0}, оверлей {_fpsOut / sec:0}, кадр оверлея {(_fpsOut > 0 ? _renderMs / _fpsOut : 0):0.00} мс{(_mcUi ? " (окно Minecraft)" : "")}");
                    if (_diagLeft > 0) { _diagLeft--; DiagRef(); }
                    CheckExclusive();
                    _fpsSl = _fpsMc = _fpsOut = 0; _renderMs = 0; _fpsAt = sw.ElapsedMilliseconds;
                }
                if (!newSl && !(_mcUi && newMc)) { Thread.Sleep(1); continue; }
                long t0 = Stopwatch.GetTimestamp();
                Render();
                _renderMs += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                _fpsOut++;
            }
        }

        private static void FindWindows()
        {
            if (_sl != null && (_sl.Closed || !Native.IsWindow(_sl.Hwnd))) { _sl.Dispose(); _sl = null; }
            if (_mc != null && (_mc.Closed || !Native.IsWindow(_mc.Hwnd))) { _mc.Dispose(); _mc = null; }

            if (_sl == null)
            {
                IntPtr h = Native.FindWindow(w => w != _hwnd && Native.ClassName(w) == _s.SlWindowClass
                                                  && Native.ProcessName(w).Equals(_s.SlProcessName, StringComparison.OrdinalIgnoreCase));
                if (h != IntPtr.Zero) TryCapture(h, ref _sl, "SCP:SL");
            }
            if (_mc == null)
            {
                IntPtr h = Native.FindWindow(w => w != _hwnd && Native.WindowText(w).StartsWith(_s.MinecraftTitlePrefix, StringComparison.Ordinal)
                                                  && Native.ProcessName(w).StartsWith("java", StringComparison.OrdinalIgnoreCase));
                if (h != IntPtr.Zero) TryCapture(h, ref _mc, "Minecraft");
            }
        }

        private static void TryCapture(IntPtr h, ref WindowCapture cap, string what)
        {
            try { cap = new WindowCapture(h, _device, _rtDevice); Log($"Захват {what}: окно {h} «{Native.WindowText(h)}»"); }
            catch (Exception e) { Log($"Не удалось захватить {what}: {e}"); cap = null; }
        }

        /// <summary>
        /// Окно Minecraft: без рамки и ровно под клиентской частью окна SL — тогда совпадают пропорции,
        /// FOV и нет чёрной полосы снизу (окно с рамкой Windows не даёт растянуть на весь экран).
        /// </summary>
        private static void FitMinecraft(Native.RECT slRect)
        {
            // На 1 пиксель ниже окна SL: окно ровно во весь экран Windows выводит «в обход» сборки рабочего стола
            // (independent flip) — тогда захват экрана в OBS видит застывший кадр окон Minecraft (верстак, инвентарь).
            // Оптимизация: в игре Minecraft можно рисовать в меньшем окне — оверлей растянет картинку (окна крафта — в полном размере).
            float sc = _mcUi ? 1f : Math.Clamp(_s.McRenderScale, 0.4f, 1f);
            if (sc < 0.999f) { slRect.Right = slRect.Left + (int)(slRect.W * sc); slRect.Bottom = slRect.Top + (int)(slRect.H * sc); }
            if (_s.AvoidFullscreenFlip) slRect.Bottom -= 1;
            if (Native.IsZoomed(_mc.Hwnd)) Native.ShowWindow(_mc.Hwnd, 9 /*SW_RESTORE*/);
            int style = Native.GetWindowLong(_mc.Hwnd, Native.GWL_STYLE);
            int strip = Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
            if ((style & strip) != 0)
            {
                Native.SetWindowLong(_mc.Hwnd, Native.GWL_STYLE, (style & ~strip) | Native.WS_POPUP);
                Native.SetWindowPos(_mc.Hwnd, Native.HWND_BOTTOM, slRect.Left, slRect.Top, slRect.W, slRect.H, Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                Log("Окно Minecraft: рамка убрана");
            }
            Native.GetWindowRect(_mc.Hwnd, out var cur);
            bool ok = cur.Left == slRect.Left && cur.Top == slRect.Top && cur.W == slRect.W && cur.H == slRect.H;
            if (!ok)
            {
                if (_mcFitAttempts >= 5) return;
                _mcFitAttempts++;
                Log($"Окно Minecraft → {slRect.Left},{slRect.Top} {slRect.W}x{slRect.H}, попытка {_mcFitAttempts}");
                Native.SetWindowPos(_mc.Hwnd, Native.HWND_BOTTOM, slRect.Left, slRect.Top, slRect.W, slRect.H, Native.SWP_NOACTIVATE | (_mcUi ? 0x0004u /*SWP_NOZORDER: оверлей должен остаться сверху*/ : 0u));
            }
            else
            {
                _mcFitAttempts = 0;
                if (!_mcUi) Native.SetWindowPos(_mc.Hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_NOMOVE | Native.SWP_NOSIZE);
            }
        }

        /// <summary>Открыт интерфейс Minecraft (верстак, печь, сундук…): показываем само окно Minecraft и отдаём ему фокус.</summary>
        private static bool _mcUi;

        private static void UpdateUiMode()
        {
            bool want = _mc != null && Native.WindowText(_mc.Hwnd).Contains("SLBRIDGE-UI");
            if (want == _mcUi) return;
            _mcUi = want;
            if (want)
            {
                Log("Интерфейс Minecraft открыт — фокус в Minecraft");
                Native.SetWindowPos(_mc.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_SHOWWINDOW);
                Native.ForceForeground(_mc.Hwnd);
            }
            else
            {
                Log("Интерфейс Minecraft закрыт — фокус в SL");
                Native.SetWindowPos(_mc.Hwnd, new IntPtr(-2) /*HWND_NOTOPMOST*/, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                Native.SetWindowPos(_mc.Hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                if (_sl != null) Native.ForceForeground(_sl.Hwnd);
            }
        }

        private static int _mcFitAttempts;

        private static void CreateWindow()
        {
            _wndProc = (h, m, w, l) =>
            {
                if (m == Native.WM_HOTKEY)
                {
                    if (w.ToInt32() == 1) _enabled = !_enabled;
                    if (w.ToInt32() == 2) _showMask = !_showMask;
                    return IntPtr.Zero;
                }
                if (m == Native.WM_DESTROY) { Native.PostQuitMessage(0); return IntPtr.Zero; }
                return Native.DefWindowProc(h, m, w, l);
            };
            var wc = new Native.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Native.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = Native.GetModuleHandle(null),
                lpszClassName = "SlBridgeCompositor",
            };
            Native.RegisterClassEx(ref wc);
            int ex = Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_NOREDIRECTIONBITMAP;
            if (_s.ClickThrough) ex |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
            _hwnd = Native.CreateWindowEx(ex, "SlBridgeCompositor", "SCP:SL x Minecraft", Native.WS_POPUP, 0, 0, 640, 360,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_s.ClickThrough) Native.SetLayeredWindowAttributes(_hwnd, 0, 255, Native.LWA_ALPHA);

            if (_s.ObsWindow)
            {
                var wc2 = wc;
                wc2.lpszClassName = "SlBridgeObsView";
                Native.RegisterClassEx(ref wc2);
                _obsHwnd = Native.CreateWindowEx(Native.WS_EX_NOACTIVATE | 0x40000 /*WS_EX_APPWINDOW*/, "SlBridgeObsView",
                    "SCP:SL x Minecraft (OBS)", Native.WS_POPUP, 0, 0, 640, 360, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            }
        }

        private static void ResizeObs(int w, int h)
        {
            if (_obsHwnd == IntPtr.Zero || (_obsSwap != null && w == _obsW && h == _obsH)) return;
            _obsRtv?.Dispose(); _obsRtv = null;
            try
            {
                if (_obsSwap == null)
                {
                    using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
                    using var adapter = dxgiDevice.GetAdapter();
                    using var factory = adapter.GetParent<IDXGIFactory2>();
                    _obsSwap = factory.CreateSwapChainForHwnd(_device, _obsHwnd, new SwapChainDescription1
                    {
                        Width = (uint)w, Height = (uint)h, Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0), BufferUsage = Usage.RenderTargetOutput,
                        BufferCount = 2, SwapEffect = SwapEffect.FlipDiscard, AlphaMode = Vortice.DXGI.AlphaMode.Ignore, Scaling = Scaling.Stretch,
                    });
                }
                else _obsSwap.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
                using (var back = _obsSwap.GetBuffer<ID3D11Texture2D>(0)) _obsRtv = _device.CreateRenderTargetView(back);
                _obsW = w; _obsH = h;
            }
            catch (Exception e) { Log("Окно для OBS: " + e.Message); _obsSwap = null; _obsHwnd = IntPtr.Zero; }
        }

        private static void InitD3D()
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out _device, out _ctx).CheckError();
            _rtDevice = WindowCapture.CreateWinRtDevice(_device);
            try
            {
                using var dx = _device.QueryInterface<IDXGIDevice>();
                using var ad = dx.GetAdapter();
                Log("Видеокарта оверлея: " + ad.Description.Description);
            }
            catch { }

            var vsBlob = Compiler.Compile(Shaders.Source, "VS", "compositor.hlsl", "vs_5_0");
            var psBlob = Compiler.Compile(Shaders.Source, "PS", "compositor.hlsl", "ps_5_0");
            _vs = _device.CreateVertexShader(vsBlob.Span);
            _ps = _device.CreatePixelShader(psBlob.Span);
            var refBlob = Compiler.Compile(Shaders.Source, "RefPS", "compositor.hlsl", "ps_5_0");
            _refPs = _device.CreatePixelShader(refBlob.Span);
            _refTex = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = 1, Height = 1, MipLevels = 1, ArraySize = 1, Format = Format.R16G16B16A16_Float,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            _refRtv = _device.CreateRenderTargetView(_refTex);
            _refSrv = _device.CreateShaderResourceView(_refTex);
            _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);
            _cb = _device.CreateBuffer(new BufferDescription((uint)sizeof(Params), BindFlags.ConstantBuffer, ResourceUsage.Default));

            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
            _dcomp.CreateTargetForHwnd(_hwnd, true, out _target).CheckError();
            _visual = _dcomp.CreateVisual();
            _target.SetRoot(_visual);
        }

        private static void ResizeSwap(int w, int h)
        {
            if (_swap != null && w == _swapW && h == _swapH) return;
            _rtv?.Dispose();
            _rtv = null;
            if (_swap == null)
            {
                using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
                using var adapter = dxgiDevice.GetAdapter();
                using var factory = adapter.GetParent<IDXGIFactory2>();
                _swap = factory.CreateSwapChainForComposition(_device, new SwapChainDescription1
                {
                    Width = (uint)w, Height = (uint)h, Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0), BufferUsage = Usage.RenderTargetOutput,
                    BufferCount = 2, SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
                    Scaling = Scaling.Stretch,
                });
                _visual.SetContent(_swap);
                _dcomp.Commit();
            }
            else
            {
                _swap.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            }
            using (var back = _swap.GetBuffer<ID3D11Texture2D>(0)) _rtv = _device.CreateRenderTargetView(back);
            _swapW = w; _swapH = h;
        }

        private static void Render()
        {
            if (_swap == null || _rtv == null || _sl.Srv == null) return;
            var p = new Params
            {
                KR = _s.KeyColor.Length > 0 ? _s.KeyColor[0] : 0, KG = _s.KeyColor.Length > 1 ? _s.KeyColor[1] : 1, KB = _s.KeyColor.Length > 2 ? _s.KeyColor[2] : 0,
                KeyThreshold = _s.KeyThreshold, KeySoftness = _s.KeySoftness, KeyMinBrightness = _s.KeyMinBrightness,
                ShowMask = (_mcUi && _mc != null && _mc.Srv != null) ? 2 : _showMask ? 1 : 0, HasMc = (_mc != null && _mc.Srv != null) ? 1 : 0,
                Adaptive = _s.AdaptiveKey ? 1 : 0,
                KeyRel = _s.KeyRelBrightness,
                CurX = -1000, CurY = -1000, CurScale = 0,
            };
            // Окно Minecraft (верстак, инвентарь): курсор Windows там не виден (его прячет SL) — рисуем свой.
            if (p.ShowMask > 1.5 && _s.DrawCursorInUi && _slRectRef != null && Native.GetCursorPos(out var cp))
            {
                var r = _slRectRef();
                p.CurX = cp.X - r.Left; p.CurY = cp.Y - r.Top;
                p.CurScale = Math.Max(1f, _swapH / 1080f);
            }
            ClientUv(_sl, out p.SlOx, out p.SlOy, out p.SlSx, out p.SlSy);
            ClientUv(_mc, out p.McOx, out p.McOy, out p.McSx, out p.McSy);
            _ctx.UpdateSubresource(in p, _cb);
            // проход 1: образец фона в текстуру 1×1
            _ctx.PSSetShaderResource(2, null);
            _ctx.OMSetRenderTargets(_refRtv);
            _ctx.RSSetViewport(new Viewport(0, 0, 1, 1));
            _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _ctx.VSSetShader(_vs);
            _ctx.PSSetShader(_refPs);
            _ctx.PSSetConstantBuffer(0, _cb);
            _ctx.PSSetSampler(0, _sampler);
            _ctx.PSSetShaderResource(0, _sl.Srv);
            _ctx.Draw(3, 0);
            // проход 2: сама картинка
            _ctx.OMSetRenderTargets(_rtv);
            _ctx.RSSetViewport(new Viewport(0, 0, _swapW, _swapH));
            _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _ctx.VSSetShader(_vs);
            _ctx.PSSetShader(_ps);
            _ctx.PSSetConstantBuffer(0, _cb);
            _ctx.PSSetSampler(0, _sampler);
            _ctx.PSSetShaderResource(0, _sl.Srv);
            _ctx.PSSetShaderResource(1, (_mc != null && _mc.Srv != null) ? _mc.Srv : _sl.Srv);
            _ctx.PSSetShaderResource(2, _refSrv);
            _ctx.Draw(3, 0);
            var pr = _swap.Present(0, PresentFlags.None);
            if (pr.Failure && !_presentErrLogged) { _presentErrLogged = true; Log("Present: ошибка " + pr); }
            if (_obsRtv != null)
            {
                _ctx.OMSetRenderTargets(_obsRtv);
                _ctx.RSSetViewport(new Viewport(0, 0, _obsW, _obsH));
                _ctx.Draw(3, 0);
                _obsSwap.Present(0, PresentFlags.None);
            }
        }
    }
}
