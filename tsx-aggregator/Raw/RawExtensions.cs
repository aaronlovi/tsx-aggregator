namespace tsx_aggregator;

internal static class RawExtensions {
    public static bool IsCompleted(this TsxCompanyProcessorFsmStates state) {
        return state is TsxCompanyProcessorFsmStates.InError or TsxCompanyProcessorFsmStates.Done;
    }
}
