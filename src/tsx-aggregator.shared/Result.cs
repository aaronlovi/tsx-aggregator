namespace tsx_aggregator.shared;

public record Result(bool Success, string ErrMsg) {
    public static readonly Result SUCCESS = new(true, string.Empty);

    public static Result SetFailure(string ErrMsg) => new(false, ErrMsg);
}

public record Result<T>(bool Success, string ErrMsg, T? Data) where T : class {

    public static Result<T> SetSuccess(T data) => new(true, string.Empty, data);

    public static Result<T> SetFailure(string ErrMsg) => new(false, ErrMsg, null);
}
