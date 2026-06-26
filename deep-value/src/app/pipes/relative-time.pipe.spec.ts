import { RelativeTimePipe } from './relative-time.pipe';

describe('RelativeTimePipe', () => {
  let pipe: RelativeTimePipe;
  // Fixed reference point so every case is deterministic.
  const now = new Date('2026-06-26T12:00:00Z');

  const ago = (ms: number) => new Date(now.getTime() - ms);
  const ahead = (ms: number) => new Date(now.getTime() + ms);
  const SEC = 1000, MIN = 60 * SEC, HOUR = 60 * MIN, DAY = 24 * HOUR;

  beforeEach(() => {
    pipe = new RelativeTimePipe();
  });

  it('returns N/A for a null value', () => {
    expect(pipe.transform(null, now)).toBe('N/A');
  });

  it('returns N/A when now is missing', () => {
    expect(pipe.transform(ago(MIN), null as unknown as Date)).toBe('N/A');
  });

  it('returns N/A for an unparseable string', () => {
    expect(pipe.transform('not-a-date', now)).toBe('N/A');
  });

  it('accepts a Date input in the past', () => {
    expect(pipe.transform(ago(5 * MIN), now)).toBe('5m 0s ago');
  });

  it('accepts an ISO string input in the past', () => {
    expect(pipe.transform(ago(5 * MIN).toISOString(), now)).toBe('5m 0s ago');
  });

  it('shows seconds only when under a minute', () => {
    expect(pipe.transform(ago(45 * SEC), now)).toBe('45s ago');
  });

  it('includes days/hours/minutes when present', () => {
    expect(pipe.transform(ago(2 * DAY + 3 * HOUR + 4 * MIN + 5 * SEC), now))
      .toBe('2d 3h 4m 5s ago');
  });

  it('omits leading zero units below the largest non-zero unit', () => {
    // 1h 0m 0s — minutes/seconds kept because hours is present, days dropped.
    expect(pipe.transform(ago(HOUR), now)).toBe('1h 0m 0s ago');
  });

  it('renders future timestamps with an "in" prefix', () => {
    expect(pipe.transform(ahead(90 * SEC), now)).toBe('in 1m 30s');
  });

  it('treats an exactly-equal timestamp as future ("in 0s")', () => {
    expect(pipe.transform(new Date(now.getTime()), now)).toBe('in 0s');
  });
});
