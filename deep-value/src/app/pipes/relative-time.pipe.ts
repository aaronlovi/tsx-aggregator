import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
    name: 'relativeTime',
    standalone: false
})
export class RelativeTimePipe implements PipeTransform {
    transform(value: Date | null, now: Date): string {
        if (!value || !now) return 'N/A';

        const diffMs = value.getTime() - now.getTime();
        const absDiffSec = Math.floor(Math.abs(diffMs) / 1000);
        const isPast = diffMs < 0;

        const days = Math.floor(absDiffSec / 86400);
        const minutes = Math.floor((absDiffSec % 86400) / 60);
        const seconds = absDiffSec % 60;

        let parts: string[] = [];
        if (days > 0) parts.push(`${days}d`);
        if (days > 0 || minutes > 0) parts.push(`${minutes}m`);
        parts.push(`${seconds}s`);

        const timeStr = parts.join(' ');
        return isPast ? `${timeStr} ago` : `in ${timeStr}`;
    }
}
