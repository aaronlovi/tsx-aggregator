import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
    name: 'relativeTime',
    standalone: false
})
export class RelativeTimePipe implements PipeTransform {
    transform(value: Date | string | null, now: Date): string {
        if (!value || !now) return 'N/A';

        // JSON deserialization gives strings; the dashboard model converts
        // explicitly, but list/details responses don't, so accept both.
        const date = value instanceof Date ? value : new Date(value);
        if (isNaN(date.getTime())) return 'N/A';

        const diffMs = date.getTime() - now.getTime();
        const absDiffSec = Math.floor(Math.abs(diffMs) / 1000);
        const isPast = diffMs < 0;

        const days = Math.floor(absDiffSec / 86400);
        const hours = Math.floor((absDiffSec % 86400) / 3600);
        const minutes = Math.floor((absDiffSec % 3600) / 60);
        const seconds = absDiffSec % 60;

        let parts: string[] = [];
        if (days > 0) parts.push(`${days}d`);
        if (days > 0 || hours > 0) parts.push(`${hours}h`);
        if (days > 0 || hours > 0 || minutes > 0) parts.push(`${minutes}m`);
        parts.push(`${seconds}s`);

        const timeStr = parts.join(' ');
        return isPast ? `${timeStr} ago` : `in ${timeStr}`;
    }
}
