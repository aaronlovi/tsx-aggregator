using System;
using tsx_aggregator.shared;

namespace dbm_persistence;
public record DbStmtResult : Result {
    private DbStmtResult(bool success, string errMsg, int numRows) : base(success, errMsg) {
        NumRows = numRows;
    }

    public int NumRows { get; init; }

    /// <summary>The exception behind a failure, if there was one: for the logs, and to tell a lost connection from an error in the SQL.</summary>
    public Exception? Exception { get; init; }

    public static DbStmtResult StatementSuccess(int numRows) {
        return new DbStmtResult(true, string.Empty, numRows);
    }

    public static DbStmtResult StatementFailure(string errMsg, Exception? exception = null) {
        return new DbStmtResult(false, errMsg, 0) { Exception = exception };
    }
}
