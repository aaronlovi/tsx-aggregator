import { InstrumentWithConflictingRawData } from "./instrument_with_conflicting_raw_data";
import { PagingData } from "./paging_data";

export class InstrumentsWithConflictingRawData {
    pagingData: PagingData;
    instrumentWithConflictingRawData: InstrumentWithConflictingRawData[];

    constructor(pagingData: PagingData, instrumentWithConflictingRawData: InstrumentWithConflictingRawData[]) {
        this.pagingData = pagingData;
        this.instrumentWithConflictingRawData = instrumentWithConflictingRawData;
    }
}