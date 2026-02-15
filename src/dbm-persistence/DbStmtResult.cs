using tsx_aggregator.shared;

namespace dbm_persistence;
public record DbStmtResult : Result {
    private DbStmtResult(bool success, string errMsg, int numRows) : base(success, errMsg) {
        NumRows = numRows;
    }

    public int NumRows { get; init; }

    public static DbStmtResult StatementSuccess(int numRows) {
        return new DbStmtResult(true, string.Empty, numRows);
    }

    public static DbStmtResult StatementFailure(string errMsg) {
        return new DbStmtResult(false, errMsg, 0);
    }
}
