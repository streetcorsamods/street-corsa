using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using D3D9 = Vortice.Direct3D9;
using D3D11 = SlimDX.Direct3D11;

namespace StreetCorsa.Controls;

/// <summary>
/// Puts what the AcTools renderer draws on a WPF surface.
///
/// The renderer works in DX11 and WPF composes in D3D9, so the two never meet directly. The renderer's
/// target is created as a shared resource; this opens that same texture on a D3D9Ex device of its own and
/// hands its surface to a <see cref="D3DImage"/>. Nothing is copied: both APIs draw on the one texture.
///
/// One of these belongs to one viewport, and the viewport disposes it when it is unloaded.
/// </summary>
public sealed partial class SharedTextureBridge : IDisposable
{
    private readonly CopyableD3DImage _image = new();

    private D3D9.IDirect3D9Ex? _d3d9;
    private D3D9.IDirect3DDevice9Ex? _device;
    private D3D9.IDirect3DTexture9? _texture;
    private D3D9.IDirect3DSurface9? _surface;
    private IntPtr _boundTarget;

    // The D3D9 adapter the device is made on: the one the renderer's DX11 device draws on, once a target has shown which
    // (MatchAdapter); the first, as before, until then or when no adapter matches
    private uint _adapter;
    private bool _matchingFailed;

    public SharedTextureBridge()
    {
        _image.IsFrontBufferAvailableChanged += (_, _) =>
        {
            // The front buffer goes away on a lock screen or a remote desktop session. When it comes back the
            // old binding is stale, so drop it and let the next present build a new one.
            if (_image.IsFrontBufferAvailable)
            {
                ReplaceDeviceIfLost();
                _boundTarget = IntPtr.Zero;
                FrontBufferRestored?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The image source to put in the visual tree</summary>
    public D3DImage Image => _image;

    /// <summary>
    /// A software copy of what is on screen, for a cut to dissolve from. Null when there is nothing to copy or the
    /// copy fails: the caller cuts instead.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? CopyFrame()
    {
        if (_boundTarget == IntPtr.Zero || !_image.IsFrontBufferAvailable) return null;

        try
        {
            var copy = _image.Copy();
            copy?.Freeze();
            return copy;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>WPF can present: false while the device is lost</summary>
    public bool IsFrontBufferAvailable => _image.IsFrontBufferAvailable;

    /// <summary>Which render target is on screen, or zero when nothing has been presented yet</summary>
    public IntPtr BoundTarget => _boundTarget;

    /// <summary>The front buffer came back and whatever is drawing should draw again</summary>
    public event EventHandler? FrontBufferRestored;

    /// <summary>
    /// Creates the D3D9Ex device. Safe to call more than once.
    /// </summary>
    public void EnsureDevice()
    {
        if (_device != null) return;

        // Into locals first: a device that cannot be made (a remote session, a driver in the middle of a reset) must
        // not leave half of itself behind for the next try to overwrite
        var d3d9 = D3D9.D3D9.Direct3DCreate9Ex();
        var parameters = new D3D9.PresentParameters
        {
            Windowed = true,
            SwapEffect = D3D9.SwapEffect.Discard,
            DeviceWindowHandle = GetDesktopWindow(),
            PresentationInterval = D3D9.PresentInterval.Immediate,
            BackBufferWidth = 1,
            BackBufferHeight = 1
        };

        try
        {
            _device = CreateDevice(d3d9, _adapter < d3d9.AdapterCount ? _adapter : 0, parameters);
        }
        catch (SharpGen.Runtime.SharpGenException) when (_adapter != 0)
        {
            // The matched adapter would not have a device: the first, as it always was, and no more matching
            _adapter = 0;
            _matchingFailed = true;
            try
            {
                _device = CreateDevice(d3d9, 0, parameters);
            }
            catch
            {
                d3d9.Dispose();
                throw;
            }
        }
        catch
        {
            d3d9.Dispose();
            throw;
        }

        _d3d9?.Dispose();
        _d3d9 = d3d9;
    }

    private static D3D9.IDirect3DDevice9Ex CreateDevice(D3D9.IDirect3D9Ex d3d9, uint adapter, D3D9.PresentParameters parameters) =>
        d3d9.CreateDeviceEx(adapter, D3D9.DeviceType.Hardware, IntPtr.Zero,
            D3D9.CreateFlags.HardwareVertexProcessing | D3D9.CreateFlags.Multithreaded | D3D9.CreateFlags.FpuPreserve,
            parameters);

    /// <summary>
    /// A D3D9Ex device is never reported lost the old way (TestCooperativeLevel always succeeds on it), so the front
    /// buffer coming back is where WPF's D3D9 interop has it asked: a device that was removed or hung is let go,
    /// with the texture on it, and the next present raises a new one. Waiting for the DX11 side to throw first would
    /// leave the picture frozen when only the D3D9 side went.
    /// </summary>
    private void ReplaceDeviceIfLost()
    {
        if (_device == null) return;

        try
        {
            // Vortice throws for a failure code (lost, hung, removed); an occluded window or a mode change is no failure
            _device.CheckDeviceState(IntPtr.Zero);
            return;
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // Let go of below
        }

        ReleaseSurface();
        _device.Dispose();
        _device = null;
        _d3d9?.Dispose();
        _d3d9 = null;
    }

    /// <summary>
    /// Shows the given render target. Rebinds first when it is not the one already on screen, which is what
    /// happens after a resize: the renderer makes a new target and the old pointer is dead.
    /// </summary>
    public void Present(IntPtr renderTarget)
    {
        if (renderTarget != _boundTarget)
        {
            Bind(renderTarget);
        }

        // Unlocked whatever happens: an image left locked never updates again
        _image.Lock();
        try
        {
            _image.AddDirtyRect(new Int32Rect(0, 0, _image.PixelWidth, _image.PixelHeight));
        }
        finally
        {
            _image.Unlock();
        }
    }

    /// <summary>
    /// Opens the renderer's shared DX11 texture on the D3D9Ex device and hands its surface to the D3DImage
    /// </summary>
    private void Bind(IntPtr renderTarget)
    {
        ReleaseSurface();

        // Owned by the renderer - do not dispose
        var texture = D3D11.Texture2D.FromPointer(renderTarget);
        var description = texture.Description;

        // On the adapter the target was drawn on; raised again if the device was let go when the front buffer came
        // back (ReplaceDeviceIfLost)
        MatchAdapter(renderTarget);
        EnsureDevice();

        IntPtr sharedHandle;
        using (var resource = new SlimDX.DXGI.Resource(texture))
        {
            sharedHandle = resource.SharedHandle;
        }

        if (sharedHandle == IntPtr.Zero)
            throw new InvalidOperationException("Render target is not a shared resource");

        _texture = _device!.CreateTexture((uint)description.Width, (uint)description.Height, 1,
            D3D9.Usage.RenderTarget, D3D9.Format.A8R8G8B8, D3D9.Pool.Default, ref sharedHandle);
        _surface = _texture.GetSurfaceLevel(0);

        _image.Lock();
        try
        {
            _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface.NativePointer);
        }
        finally
        {
            _image.Unlock();
        }

        _boundTarget = renderTarget;
    }

    /// <summary>
    /// On a machine of more than one GPU (a laptop's two, an external card) the renderer's DX11 device may not be on the
    /// first adapter, and a shared handle opens only on the adapter it was made on: the D3D9 device goes where the
    /// target's device is, found by the adapters' LUIDs. Anything unclear (the adapter cannot be asked, no D3D9 adapter
    /// has that LUID) leaves the device where it is.
    /// </summary>
    private void MatchAdapter(IntPtr renderTarget)
    {
        if (_matchingFailed) return;

        uint? wanted;
        try
        {
            if (AdapterLuidOf(renderTarget) is not { } luid) return;

            var d3d9 = _d3d9 ?? D3D9.D3D9.Direct3DCreate9Ex();
            try
            {
                var adapters = new List<long>();
                for (uint i = 0; i < d3d9.AdapterCount; i++) adapters.Add(LuidOf(d3d9.GetAdapterLuid(i)));
                wanted = AdapterOf(luid, adapters);
            }
            finally
            {
                if (!ReferenceEquals(d3d9, _d3d9)) d3d9.Dispose();
            }
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or COMException)
        {
            return;
        }

        if (wanted is not { } index || index == _adapter) return;

        // A device made on another adapter goes; the next one is made on this
        _adapter = index;
        _device?.Dispose();
        _device = null;
    }

    /// <summary>The index of the adapter of <paramref name="luid"/> among <paramref name="adapters"/>' LUIDs; null when none has it</summary>
    internal static uint? AdapterOf(long luid, IReadOnlyList<long> adapters)
    {
        for (var i = 0; i < adapters.Count; i++)
            if (adapters[i] == luid) return (uint)i;
        return null;
    }

    private static long LuidOf(D3D9.Luid luid) => ((long)luid.HighPart << 32) | (uint)luid.LowPart;

    // IDXGIDevice
    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// <summary>
    /// DXGI_ADAPTER_DESC as dxgi.h lays it out (304 bytes on x64, the LUID at 296): only the LUID is read
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct AdapterDescription
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
    }

    /// <summary>
    /// The LUID of the adapter a DX11 texture was made on, asked of its device straight through COM: each reference
    /// taken here is let go of here, and nothing the renderer holds is touched (the wrappers SlimDX hands out for a
    /// device or an adapter may be the renderer's own). Null when any step fails.
    /// </summary>
    private static unsafe long? AdapterLuidOf(IntPtr texture)
    {
        if (texture == IntPtr.Zero) return null;

        IntPtr device = IntPtr.Zero, dxgi = IntPtr.Zero, adapter = IntPtr.Zero;
        try
        {
            // ID3D11DeviceChild::GetDevice, after IUnknown's three
            var table = *(IntPtr**)texture;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)table[3])(texture, &device);
            if (device == IntPtr.Zero || Marshal.QueryInterface(device, in DxgiDeviceId, out dxgi) != 0 || dxgi == IntPtr.Zero) return null;

            // IDXGIDevice::GetAdapter, after IUnknown's three and IDXGIObject's four
            table = *(IntPtr**)dxgi;
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)table[7])(dxgi, &adapter) < 0 || adapter == IntPtr.Zero) return null;

            // IDXGIAdapter::GetDesc, after IDXGIObject's seven and EnumOutputs
            AdapterDescription description;
            table = *(IntPtr**)adapter;
            if (((delegate* unmanaged[Stdcall]<IntPtr, AdapterDescription*, int>)table[8])(adapter, &description) < 0) return null;

            return ((long)description.LuidHighPart << 32) | description.LuidLowPart;
        }
        finally
        {
            if (adapter != IntPtr.Zero) Marshal.Release(adapter);
            if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
            if (device != IntPtr.Zero) Marshal.Release(device);
        }
    }

    /// <summary>
    /// Lets go of the texture on screen, leaving the device up. Call before the renderer that owns the
    /// target is disposed.
    /// </summary>
    public void ReleaseSurface()
    {
        if (_boundTarget != IntPtr.Zero)
        {
            _image.Lock();
            try
            {
                _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
            }
            finally
            {
                _image.Unlock();
            }
        }

        _surface?.Dispose();
        _surface = null;
        _texture?.Dispose();
        _texture = null;
        _boundTarget = IntPtr.Zero;
    }

    public void Dispose()
    {
        ReleaseSurface();

        _device?.Dispose();
        _device = null;
        _d3d9?.Dispose();
        _d3d9 = null;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDesktopWindow();
}

/// <summary>D3DImage keeps its read-back protected; this lets the bridge take a still of the picture</summary>
internal sealed class CopyableD3DImage : D3DImage
{
    public System.Windows.Media.Imaging.BitmapSource? Copy() => CopyBackBuffer();

    protected override Freezable CreateInstanceCore() => new CopyableD3DImage();
}
