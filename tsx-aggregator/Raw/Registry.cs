using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using tsx_aggregator.models;

namespace tsx_aggregator.Raw;

internal class Registry {

    private readonly List<InstrumentDto> _instruments; // Sorted by company symbol then instrument symbol

	public Registry() {
		_instruments = new List<InstrumentDto>();
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
    // If given 'prevCompanyAndInstrumentSymbol' is the last symbol, then returns the first symbol in the instrument list
    public InstrumentKey? GetNextInstrumentKey(InstrumentKey prevInstrumentKey) {
        InstrumentKey? firstCompanyAndInstrumentSymbol = null;

        foreach (InstrumentDto instrument in _instruments) {
            var curKey = new InstrumentKey(instrument.CompanySymbol, instrument.InstrumentSymbol, instrument.Exchange);
            firstCompanyAndInstrumentSymbol ??= curKey;
            int compareRes = InstrumentKey.CompareBySymbols(prevInstrumentKey, curKey);
            if (compareRes < 0)
                return curKey;
        }

        return firstCompanyAndInstrumentSymbol;
    }

    // Gets an instrument from the directory by looking up the given 'CompanyAndInstrumentSymbol'
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
}
