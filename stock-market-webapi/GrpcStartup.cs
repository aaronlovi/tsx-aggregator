namespace stock_market_webapi;

public class GrpcStartup {
    private IConfiguration _config;

    public GrpcStartup(IConfiguration config)
    {
        _config = config;
    }

    public void ConfigureServices(IServiceCollection services) {
        services.AddGrpc(options => {
            // Limit the size of client requests
            options.MaxReceiveMessageSize = 64 * 1024;
        });
    }

    public void Configure(IApplicationBuilder app) {
        app.UseRouting();
    }
}
