using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using VRCHOTAS.Logging;

namespace VRCHOTAS.Interop;

public sealed class SharedMemoryStateChannel : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly MemoryMappedFile _memory;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Mutex _mutex;
    private readonly int _stateSize;
    private bool _disposed;

    public SharedMemoryStateChannel(IAppLogger logger)
    {
        _logger = logger;
        _stateSize = Marshal.SizeOf<VirtualControllerState>();
        _memory = MemoryMappedFile.CreateOrOpen(VirtualControllerLayout.SharedMemoryName, _stateSize, MemoryMappedFileAccess.ReadWrite);
        _view = _memory.CreateViewAccessor(0, _stateSize, MemoryMappedFileAccess.ReadWrite);
        _mutex = new Mutex(false, VirtualControllerLayout.SharedMemoryMutexName);
        _logger.Info(nameof(SharedMemoryStateChannel), "Shared memory channel initialized.");
    }

    public void Write(in VirtualControllerState state)
    {
        ThrowIfDisposed();

        var copy = state;
        copy.EnsureInitialized();

        if (!_mutex.WaitOne(5))
        {
            _logger.Warning(nameof(SharedMemoryStateChannel), "Shared memory write timed out. Frame skipped.");
            return;
        }

        try
        {
            var existing = ReadStructFromView();
            copy.DriverHeartbeatTickMs = existing.DriverHeartbeatTickMs;
            var bytes = StructureToBytes(copy);
            _view.WriteArray(0, bytes, 0, bytes.Length);
            _view.Flush();
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(SharedMemoryStateChannel), "Shared memory write failed.", ex);
            throw;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public bool TryReadDriverHeartbeat(out ulong tickMs)
    {
        tickMs = 0;
        ThrowIfDisposed();

        if (!_mutex.WaitOne(5))
        {
            return false;
        }

        try
        {
            var state = ReadStructFromView();
            tickMs = state.DriverHeartbeatTickMs;
            return true;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    private VirtualControllerState ReadStructFromView()
    {
        var bytes = new byte[_stateSize];
        _view.ReadArray(0, bytes, 0, _stateSize);
        var ptr = Marshal.AllocHGlobal(_stateSize);
        try
        {
            Marshal.Copy(bytes, 0, ptr, _stateSize);
            return Marshal.PtrToStructure<VirtualControllerState>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static byte[] StructureToBytes(VirtualControllerState state)
    {
        var size = Marshal.SizeOf<VirtualControllerState>();
        var bytes = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(state, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SharedMemoryStateChannel));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.Dispose();
        _memory.Dispose();
        _mutex.Dispose();
        _logger.Info(nameof(SharedMemoryStateChannel), "Shared memory channel disposed.");
        GC.SuppressFinalize(this);
    }
}
