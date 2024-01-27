using System.Collections.Generic;

namespace tsx_aggregator.shared;

public class TrieNode<T> {
    public Dictionary<char, TrieNode<T>> Children { get; }
    public ISet<T> Keys { get; }

    public TrieNode() {
        Children = new();
        Keys = new HashSet<T>();
    }
}
