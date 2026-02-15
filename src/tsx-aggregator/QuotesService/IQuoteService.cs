using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace tsx_aggregator.Services;

internal interface IQuoteService : IHostedService {
    DateTime? NextFetchQuotesTime { get; }
    TaskCompletionSource QuoteServiceReady { get; }
    bool PostRequest(QuoteServiceInputBase inp);
}
