export class PagingData {
    totalItems: number;
    pageNumber: number;
    pageSize: number;

    constructor(totalItems: number, pageNumber: number, pageSize: number) {
        this.totalItems = totalItems;
        this.pageNumber = pageNumber;
        this.pageSize = pageSize;
    }

    get numPages() {
        return Math.ceil(this.totalItems / this.pageSize);
    }
}