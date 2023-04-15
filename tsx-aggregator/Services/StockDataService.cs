using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;

namespace tsx_aggregator.Services;
public class StockDataSvc : StockDataService.StockDataServiceBase {
    public override Task<GetStocksDataReply> GetStocksData(GetStocksDataRequest request, ServerCallContext context) {
        return base.GetStocksData(request, context);
    }
}
