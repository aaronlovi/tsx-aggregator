using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static tsx_aggregator.Services.StockDataService;

namespace stock_market_webapi;

public class Program {
    public static void Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);

        _ = builder.Services.AddCors(options => {
            options.AddPolicy("AllowAll", builder => {
                _ = builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        // Add services to the container.
        _ = builder.Services.AddControllers();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        _ = builder.Services.AddEndpointsApiExplorer();
        _ = builder.Services.AddSwaggerGen(c => {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "Stock Market API", Version = "v1" });
        });

        _ = builder.Services.AddGrpc();
        _ = builder.Services.AddGrpcReflection();
        _ = builder.Services.AddGrpcClient<StockDataServiceClient>(options => {
            options.Address = new Uri("http://localhost:7001");
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment()) {
            _ = app.UseSwagger();
            _ = app.UseSwaggerUI(c => {
                c.SwaggerEndpoint("v1/swagger.json", "Stock Market API V1");
            });
        }

        _ = app.UseCors(x => x
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());

        _ = app.UseHttpsRedirection();

        _ = app.UseAuthorization();

        //app.UseCors("AllowAllOrigins");

        _ = app.MapControllers();

        app.Run();
    }
}
