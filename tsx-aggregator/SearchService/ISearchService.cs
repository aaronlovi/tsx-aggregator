using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace tsx_aggregator.Services;
public interface ISearchService : IHostedService {
    TaskCompletionSource SearchServiceReady { get; }

    bool PostRequest(SearchServiceInputBase inp);
}