import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
    name: 'compactCurrency'
})
export class CompactCurrencyPipe implements PipeTransform {
    transform(value: number | null): string {
        if (value == null) return 'N/A';
        const abs = Math.abs(value);
        const sign = value < 0 ? '-' : '';
        if (abs >= 1e12) return `${sign}$${(abs / 1e12).toFixed(3)}T`;
        if (abs >= 1e9) return `${sign}$${(abs / 1e9).toFixed(3)}B`;
        if (abs >= 1e6) return `${sign}$${(abs / 1e6).toFixed(3)}M`;
        if (abs >= 1e3) return `${sign}$${(abs / 1e3).toFixed(1)}K`;
        return `${sign}$${abs.toFixed(0)}`;
    }
}
