export class Utilities {
    public static DivSafe(numerator: number, denominator: number | null | undefined, defaultValue: number = Number.MAX_VALUE): number {
        if (denominator === 0 || denominator == null || denominator === undefined || !Number.isFinite(denominator)) {
            return defaultValue;
        }

        return numerator / denominator;
    }
}
