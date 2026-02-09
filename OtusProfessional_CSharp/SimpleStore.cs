namespace OtusProfessional_CSharp;

public class SimpleStore
{
    private readonly Dictionary<string, byte[]> _store = new();

    public void Set(string key, byte[] value)
    {
        if (!_store.TryAdd(key, value))
            _store[key] = value;
    }
    
    public byte[] Get(string key) => 
        _store[key];
    
    public void Delete(string key) => 
        _store.Remove(key);
}