using System.Collections.Generic;

namespace tsx_aggregator.shared;

public class NormalizedStringKeysHashMap<T> where T : struct {
    private readonly Dictionary<string, T> _dataMap;

    public NormalizedStringKeysHashMap() {
        _dataMap = new();
    }

    public T? this[string key] {
        get {
            var res = _dataMap.TryGetValue(key.ToUpperInvariant(), out T val);
            return res ? val : null;
        }
        set {
            if (value is not null)
                _dataMap[key.ToUpperInvariant()] = value.Value;
            else
                _ = _dataMap.Remove(key.ToUpperInvariant());
        }
    }

    public bool HasValue(string key) {
        return _dataMap.ContainsKey(key.ToUpperInvariant());
    }

    public Dictionary<string, T>.KeyCollection Keys => _dataMap.Keys;
}
