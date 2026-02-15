using System;

namespace tsx_aggregator.models;

public record PagingData(int TotalItems, int PageNumber, int PageSize) {
    public int NumPages => (int)Math.Ceiling((double)TotalItems / PageSize);
}
