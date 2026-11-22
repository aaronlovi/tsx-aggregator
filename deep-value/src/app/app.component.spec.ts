import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { RouterTestingModule } from '@angular/router/testing';
import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { Subject } from 'rxjs';
import { AppComponent } from './app.component';

describe('AppComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [RouterTestingModule],
    declarations: [AppComponent],
    // The shell renders <app-header> and Material sidenav elements that aren't
    // declared in this lightweight test module; ignore the unknown elements.
    schemas: [NO_ERRORS_SCHEMA]
  }));

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should default the sidenav to open', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app.sidenavOpened).toBe(true);
  });

  describe('on a narrow screen (phone)', () => {
    let breakpoints: Subject<BreakpointState>;

    beforeEach(() => {
      breakpoints = new Subject<BreakpointState>();
      TestBed.overrideProvider(BreakpointObserver, {
        useValue: { observe: () => breakpoints.asObservable() }
      });
    });

    it('starts with the menu closed and slides it over the page', () => {
      const fixture = TestBed.createComponent(AppComponent);
      const app = fixture.componentInstance;

      breakpoints.next({ matches: true, breakpoints: {} });

      expect(app.isNarrow).toBe(true);
      expect(app.sidenavOpened).toBe(false);
      expect(app.sidenavMode).toBe('over');
    });

    it('closes the menu after a page is picked', () => {
      const fixture = TestBed.createComponent(AppComponent);
      const app = fixture.componentInstance;
      breakpoints.next({ matches: true, breakpoints: {} });
      app.sidenavOpened = true;

      app.onNavigate();

      expect(app.sidenavOpened).toBe(false);
    });

    it('keeps the desktop layout when the screen is wide', () => {
      const fixture = TestBed.createComponent(AppComponent);
      const app = fixture.componentInstance;

      breakpoints.next({ matches: false, breakpoints: {} });
      app.onNavigate();

      expect(app.isNarrow).toBe(false);
      expect(app.sidenavOpened).toBe(true);
      expect(app.sidenavMode).toBe('side');
    });
  });
});
