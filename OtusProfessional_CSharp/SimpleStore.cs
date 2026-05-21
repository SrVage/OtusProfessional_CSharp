using System.Text.Json;
using Common;

namespace OtusProfessional_CSharp;

public class SimpleStore : IDisposable
{
    private readonly Dictionary<string, byte[]> _store = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed = false;
    private long _setCount;
    private long _getCount;
    private long _deleteCount;

    public void Set(string key, UserProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lock.EnterWriteLock();
        
        try
        {
            //var profileValue = JsonSerializer.SerializeToUtf8Bytes(profile);
            var profileValue = profile.SerializeToBinary();
            if (!_store.TryAdd(key, profileValue))
                _store[key] = profileValue;
            Interlocked.Increment(ref _setCount);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    public UserProfile? Get(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lock.EnterReadLock();
        
        try
        {
            var getValue = _store.GetValueOrDefault(key);
            Interlocked.Increment(ref _getCount);
            var profile = UserProfile.DeserializeFromBinary(getValue);
            return profile;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Delete(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lock.EnterWriteLock();
        
        try
        {
            _store.Remove(key);
            Interlocked.Increment(ref _deleteCount);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public (long, long, long) GetStatistics()
    {
        return (_setCount, _getCount, _deleteCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        _lock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}