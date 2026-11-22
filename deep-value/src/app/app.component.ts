import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { BreakpointObserver } from '@angular/cdk/layout';

/** Screens up to this width (phones, small tablets) get the overlay menu. */
export const NARROW_SCREEN_QUERY = '(max-width: 768px)';

@Component({
    selector: 'app-root',
    templateUrl: './app.component.html',
    styleUrls: ['./app.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class AppComponent {
    sidenavOpened = true;
    isNarrow = false;

    constructor() {
        // On a phone the menu would take over half the screen: start it closed and slide it over the page.
        inject(BreakpointObserver).observe(NARROW_SCREEN_QUERY)
            .pipe(takeUntilDestroyed())
            .subscribe(state => {
                this.isNarrow = state.matches;
                this.sidenavOpened = !state.matches;
            });
    }

    get sidenavMode(): 'over' | 'side' {
        return this.isNarrow ? 'over' : 'side';
    }

    /** A menu link was picked: on a phone, get the menu out of the way. */
    onNavigate(): void {
        if (this.isNarrow) {
            this.sidenavOpened = false;
        }
    }
}
