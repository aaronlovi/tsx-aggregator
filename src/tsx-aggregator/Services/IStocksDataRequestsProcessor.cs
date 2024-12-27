using Microsoft.Extensions.Hosting;

namespace tsx_aggregator.Services;

public interface IStocksDataRequestsProcessor : IHostedService {
    bool PostRequest(StocksDataRequestsInputBase inp);
}
