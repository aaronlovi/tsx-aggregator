import { describe, expect, it } from 'vitest';
import { changeErrorMessage, READ_ONLY_MESSAGE } from './system-controls.component';

describe('changeErrorMessage', () => {
    it('explains a 405 from the read-only web API', () => {
        expect(changeErrorMessage({ status: 405 }, 'Failed to set priority companies')).toBe(READ_ONLY_MESSAGE);
    });

    it('keeps the fallback for other failures', () => {
        expect(changeErrorMessage({ status: 500 }, 'Failed to set priority companies')).toBe('Failed to set priority companies');
        expect(changeErrorMessage(null, 'Failed to clear priority queue')).toBe('Failed to clear priority queue');
    });
});
