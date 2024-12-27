namespace stock_market_webapi;

public class GrpcStartup {
    public void ConfigureServices(IServiceCollection services) {
        services.AddGrpc(options => {
            // Limit the size of client requests
            options.MaxReceiveMessageSize = 64 * 1024;
        });
    }

    public void Configure(IApplicationBuilder app) => app.UseRouting();
}
