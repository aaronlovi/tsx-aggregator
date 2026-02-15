using System.ComponentModel.DataAnnotations;

namespace tsx_aggregator.models;

public class HostedServicesOptions {
    public const string HostedServices = "HostedServices";

    public HostedServicesOptions() { }

    [Required(ErrorMessage = "RunAggregator is required in the HostedServices section of the configuration")]
    public bool? RunAggregator { get; set; }

    [Required(ErrorMessage = "RunRawCollector is required in the HostedServices section of the configuration")]
    public bool? RunRawCollector { get; set; }

    [Required(ErrorMessage = "RunStocksDataRequestsProcessor is required in the HostedServices section of the configuration")]
    public bool? RunStocksDataRequestsProcessor { get; set; }

    [Required(ErrorMessage = "RunQuoteService is required in the HostedServices section of the configuration")]
    public bool? RunQuoteService { get; set; }

    [Required(ErrorMessage = "RunSearchService is required in the HostedServices section of the configuration")]
    public bool? RunSearchService { get; set; }

    [Required(ErrorMessage = "RunScore13AlertService is required in the HostedServices section of the configuration")]
    public bool? RunScore13AlertService { get; set; }
}
