using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace SlBridge.Compositor
{
    /// <summary>Захват содержимого окна через Windows Graphics Capture (работает, даже если окно перекрыто).</summary>
    internal sealed unsafe class WindowCapture : IDisposable
    {
        private static readonly Guid IID_IGraphicsCaptureItemInterop = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        private static readonly Guid IID_IGraphicsCaptureItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        private static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        public readonly IntPtr Hwnd;
        private readonly ID3D11Device _device;
        private readonly IDirect3DDevice _rtDevice;
        private GraphicsCaptureItem _item;
        private Direct3D11CaptureFramePool _pool;
        private GraphicsCaptureSession _session;
        private Windows.Graphics.SizeInt32 _poolSize;

        public ID3D11Texture2D Texture { get; private set; }
        public ID3D11ShaderResourceView Srv { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool Closed { get; private set; }

        public WindowCapture(IntPtr hwnd, ID3D11Device device, IDirect3DDevice rtDevice)
        {
            Hwnd = hwnd;
            _device = device;
            _rtDevice = rtDevice;
            _item = CreateItemForWindow(hwnd);
            _item.Closed += (_, _) => Closed = true;
            _poolSize = _item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_rtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
            _session = _pool.CreateCaptureSession(_item);
            try { _session.IsCursorCaptureEnabled = false; } catch { }
            try { _session.IsBorderRequired = false; } catch { } // Windows 11: без жёлтой рамки
            _session.StartCapture();
        }

        public static IDirect3DDevice CreateWinRtDevice(ID3D11Device device)
        {
            using var dxgi = device.QueryInterface<IDXGIDevice>();
            Marshal.ThrowExceptionForHR(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out IntPtr insp));
            var d = MarshalInterface<IDirect3DDevice>.FromAbi(insp);
            Marshal.Release(insp);
            return d;
        }

        private static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
        {
            const string cls = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(Native.WindowsCreateString(cls, cls.Length, out IntPtr hs));
            try
            {
                Guid iid = IID_IGraphicsCaptureItemInterop;
                Marshal.ThrowExceptionForHR(Native.RoGetActivationFactory(hs, ref iid, out IntPtr interop));
                try
                {
                    // IGraphicsCaptureItemInterop::CreateForWindow — слот 3 (после IUnknown).
                    var vtbl = *(IntPtr**)interop;
                    var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[3];
                    Guid itemIid = IID_IGraphicsCaptureItem;
                    IntPtr itemPtr;
                    Marshal.ThrowExceptionForHR(fn(interop, hwnd, &itemIid, &itemPtr));
                    var item = GraphicsCaptureItem.FromAbi(itemPtr);
                    Marshal.Release(itemPtr);
                    return item;
                }
                finally { Marshal.Release(interop); }
            }
            finally { Native.WindowsDeleteString(hs); }
        }

        /// <summary>Забрать самый свежий кадр (если есть) и скопировать его в свою текстуру. true — кадр обновился.</summary>
        public bool Poll(ID3D11DeviceContext ctx)
        {
            Direct3D11CaptureFrame frame = null, next;
            while ((next = _pool.TryGetNextFrame()) != null)
            {
                frame?.Dispose();
                frame = next;
            }
            if (frame == null) return false;
            using (frame)
            {
                var size = frame.ContentSize;
                if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
                {
                    _poolSize = size;
                    _pool.Recreate(_rtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                }
                using var src = GetTexture(frame.Surface);
                if (src == null) return false;
                var desc = src.Description;
                int w = Math.Min(size.Width, (int)desc.Width), h = Math.Min(size.Height, (int)desc.Height);
                if (w <= 0 || h <= 0) return false;
                EnsureTexture(w, h);
                ctx.CopySubresourceRegion(Texture, 0, 0, 0, 0, src, 0, new Vortice.Mathematics.Box(0, 0, 0, w, h, 1));
                return true;
            }
        }

        private static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
        {
            IntPtr abi = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
            try
            {
                Guid iid = IID_IDirect3DDxgiInterfaceAccess;
                if (Marshal.QueryInterface(abi, ref iid, out IntPtr access) != 0) return null;
                try
                {
                    var vtbl = *(IntPtr**)access;
                    var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtbl[3];
                    Guid tex = IID_ID3D11Texture2D;
                    IntPtr texPtr;
                    if (getInterface(access, &tex, &texPtr) != 0) return null;
                    return new ID3D11Texture2D(texPtr);
                }
                finally { Marshal.Release(access); }
            }
            finally { Marshal.Release(abi); }
        }

        private void EnsureTexture(int w, int h)
        {
            if (Texture != null && Width == w && Height == h) return;
            Srv?.Dispose();
            Texture?.Dispose();
            Width = w; Height = h;
            Texture = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource,
            });
            Srv = _device.CreateShaderResourceView(Texture);
        }

        public void Dispose()
        {
            try { _session?.Dispose(); } catch { }
            try { _pool?.Dispose(); } catch { }
            Srv?.Dispose();
            Texture?.Dispose();
        }
    }
}
