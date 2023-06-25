using System;
using Microsoft.Extensions.Hosting;

namespace tsx_aggregator.Services;

internal interface IQuoteService : IHostedService {
    DateTime? NextFetchQuotesTime { get; }
    bool PostRequest(QuoteServiceInputBase inp);
}
