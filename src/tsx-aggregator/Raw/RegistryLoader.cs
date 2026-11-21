using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator.Raw;

/// <summary>
/// Loads the instrument directory from the database into the <see cref="Registry"/> at startup, before the other
/// hosted services start (it is registered first, and hosts start services in registration order).
/// The QuoteService and SearchService wait for the registry, so loading it here rather than in the RawCollector
/// means the API, quotes and search work with the RawCollector turned off (e.g. while scraping is not wanted).
/// </summary>
internal sealed class RegistryLoader : IHostedService {
    private readonly Registry _registry;
    private readonly IDbmService _dbm;
    private readonly bool _rawCollectorEnabled;
    private readonly ILogger<RegistryLoader> _logger;

    public RegistryLoader(Registry registry, IDbmService dbm, IOptions<HostedServicesOptions> hostedServices, ILogger<RegistryLoader> logger) {
        _registry = registry;
        _dbm = dbm;
        _rawCollectorEnabled = hostedServices.Value.RunRawCollector ?? false;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken) {
        (Result res, IReadOnlyList<InstrumentDto> instruments) = await _dbm.GetInstrumentList(cancellationToken);
        if (!res.Success)
            throw new InvalidOperationException("RegistryLoader - failed to load the instrument directory: " + res.ErrMsg);

        if (instruments.Count > 0) {
            _registry.InitializeDirectory(instruments);
            _logger.LogInformation("RegistryLoader - loaded {Count} instruments", instruments.Count);
            return;
        }

        if (_rawCollectorEnabled) {
            // First run: the RawCollector fetches the directory from the TSX and signals the registry when done.
            _logger.LogWarning("RegistryLoader - no instruments in the database; waiting for the RawCollector to fetch the directory");
            return;
        }

        // Nothing will ever fill the registry, so signal it empty rather than leave dependent services waiting forever.
        _registry.InitializeDirectory(instruments);
        _logger.LogWarning("RegistryLoader - no instruments in the database and the RawCollector is off; the registry is empty");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
