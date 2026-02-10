using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;

namespace tsx_aggregator.Raw;

internal class Registry {

    private readonly List<InstrumentDto> _instruments; // Sorted by company symbol then instrument symbol
    private readonly Queue<string> _priorityCompanySymbols;
    private readonly object _priorityLock;
    private readonly ILogger? _logger;

	public Registry() : this(null) { }

    public Registry(ILogger? logger) {
		_instruments = new();
        _priorityCompanySymbols = new();
        _priorityLock = new object();
        _logger = logger;
        DirectoryInitialized = new TaskCompletionSource();
	}

    /// <summary>
    /// Set when the list of instruments has been initialized
    /// </summary>
    public TaskCompletionSource DirectoryInitialized { get; }

    public int NumInstruments {
		get {
            lock (_instruments)
                return _instruments.Count;
        }
    }

    public void InitializeDirectory(IReadOnlyList<InstrumentDto> directory) {
        lock (_instruments) {
            foreach (var instrument in directory)
                _instruments.Add(instrument);

            _instruments.Sort(InstrumentKey.CompareBySymbols);
            
            // Notify subscribers that the list of instruments has been initialized
            DirectoryInitialized.TrySetResult();
        }
    }

    public void AddInstrument(InstrumentDto newInstrument) {
        lock (_instruments) {
            int index = _instruments.BinarySearch(newInstrument, InstrumentKey.ComparerBySymbols);
            if (index >= _instruments.Count) {
                _instruments.Add(newInstrument);
            } else if (index < 0) {
                _instruments.Insert(~index, newInstrument);
            }
        }
    }

    public void RemoveInstrument(InstrumentDto obsoletedInstrument) {
        lock (_instruments) {
            int index = _instruments.BinarySearch(obsoletedInstrument, InstrumentKey.ComparerBySymbols);
            if (index < 0)
                return;
            _instruments.RemoveAt(index);
        }
    }

    public IList<InstrumentDto> GetNewInstruments(IReadOnlyDictionary<string, InstrumentDtoByInstrumentNameMap> newDirectory) {
        IList<InstrumentDto> newInstrumentList = new List<InstrumentDto>();

        lock (_instruments) {
            foreach (var instrumentMap in newDirectory.Values) {
                foreach (var instrument in instrumentMap.Values) {
                    int index = _instruments.BinarySearch(instrument, InstrumentKey.ComparerBySymbols);
                    if (index < 0)
                        newInstrumentList.Add(instrument);
                }
            }
        }

        return newInstrumentList;
    }

    public IList<InstrumentDto> GetObsoletedInstruments(IReadOnlyDictionary<string, InstrumentDtoByInstrumentNameMap> newDirectory) {
        IList<InstrumentDto> obsoletedInstrumentList = new List<InstrumentDto>();

        lock (_instruments) {
            foreach (InstrumentDto instrument in _instruments) {
                if (!newDirectory.ContainsKey(instrument.CompanySymbol)) {
                    // Parent company no longer exists
                    obsoletedInstrumentList.Add(instrument);
                    continue;
                }

                IReadOnlyDictionary<string, InstrumentDto> instrumentMap = newDirectory[instrument.CompanySymbol];
                if (!instrumentMap.ContainsKey(instrument.InstrumentSymbol))
                    obsoletedInstrumentList.Add(instrument);
            }
        }

        return obsoletedInstrumentList;
    }

    // Gets the next symbol in the instrument list.
    // If given 'prevInstrumentKey' is the last symbol, then returns the first symbol in the instrument list
    public InstrumentKey? GetNextInstrumentKey(InstrumentKey prevInstrumentKey) {
        InstrumentKey? firstInstrumentKey = null;

        foreach (InstrumentDto instrument in _instruments) {
            var curKey = new InstrumentKey(instrument.CompanySymbol, instrument.InstrumentSymbol, instrument.Exchange);
            firstInstrumentKey ??= curKey;
            int compareRes = InstrumentKey.CompareBySymbols(prevInstrumentKey, curKey);
            if (compareRes < 0)
                return curKey;
        }

        return firstInstrumentKey;
    }

    // Gets an instrument from the directory by looking up the given 'InstrumentKey'
    // May return undefined if the instrument is not found
    public InstrumentDto? GetInstrument(InstrumentKey k) {
        lock (_instruments) {
            var dummy = new InstrumentDto(0, k.Exchange, k.CompanySymbol, string.Empty, k.InstrumentSymbol, string.Empty, DateTimeOffset.UtcNow, null);
            int index = _instruments.BinarySearch(dummy, InstrumentKey.ComparerBySymbols);
            return index >= 0 ? _instruments[index] : null;
        }
    }

    public IReadOnlyCollection<InstrumentDto> GetInstruments() {
        lock (_instruments) {
            return _instruments.ToArray();
        }
    }

    /// <summary>
    /// Replaces the current priority queue with the given company symbols (deduped, order-preserved).
    /// Returns the count of symbols that match known instruments.
    /// </summary>
    public int SetPriorityCompanies(IReadOnlyList<string> companySymbols) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<string>();
        foreach (var symbol in companySymbols) {
            if (seen.Add(symbol))
                deduped.Add(symbol);
        }

        int validCount = 0;
        lock (_priorityLock) {
            _priorityCompanySymbols.Clear();
            foreach (var symbol in deduped) {
                _priorityCompanySymbols.Enqueue(symbol);
                if (FindFirstInstrumentByCompanySymbol(symbol) is not null)
                    validCount++;
            }
        }

        return validCount;
    }

    /// <summary>
    /// Dequeues the next priority company symbol and resolves it to an InstrumentKey.
    /// Skips unknown symbols. Returns true if a key was found, false if queue is empty or all remaining symbols were invalid.
    /// </summary>
    public bool TryDequeueNextPriorityInstrumentKey(out InstrumentKey? key) {
        lock (_priorityLock) {
            while (_priorityCompanySymbols.Count > 0) {
                string symbol = _priorityCompanySymbols.Dequeue();
                var instrument = FindFirstInstrumentByCompanySymbol(symbol);
                if (instrument is not null) {
                    key = new InstrumentKey(instrument.CompanySymbol, instrument.InstrumentSymbol, instrument.Exchange);
                    return true;
                }

                _logger?.LogWarning("Priority company symbol '{Symbol}' not found in instrument list, skipping", symbol);
            }
        }

        key = null;
        return false;
    }

    /// <summary>
    /// Returns a snapshot of the current priority queue contents without dequeuing.
    /// </summary>
    public IReadOnlyList<string> GetPriorityCompanySymbols() {
        lock (_priorityLock) {
            return _priorityCompanySymbols.ToArray();
        }
    }

    /// <summary>
    /// Clears the priority queue.
    /// </summary>
    public void ClearPriorityCompanies() {
        lock (_priorityLock) {
            _priorityCompanySymbols.Clear();
        }
    }

    private InstrumentDto? FindFirstInstrumentByCompanySymbol(string companySymbol) {
        lock (_instruments) {
            foreach (var instrument in _instruments) {
                if (string.Equals(instrument.CompanySymbol, companySymbol, StringComparison.Ordinal))
                    return instrument;
            }
        }
        return null;
    }
}
