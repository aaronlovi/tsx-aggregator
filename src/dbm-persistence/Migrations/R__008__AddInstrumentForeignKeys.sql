-- Every table below carries an instrument_id, but none of them constrained it. When a
-- re-listed instrument's INSERT was silently discarded by "ON CONFLICT DO NOTHING", the
-- RawCollector kept the freshly allocated id in its in-memory registry and went on writing
-- events, reports and prices against an instrument row that was never created. Nothing
-- failed, because nothing was checking: the writes simply landed in a hole and stayed
-- unreadable, since every read path joins back through instruments.
--
-- These constraints make that failure mode impossible to repeat -- a write against an
-- unknown instrument_id now aborts its transaction instead of vanishing.
--
-- NOTE: this migration fails if the database still holds orphaned rows from that era.
-- Clear them first, per table:
--   DELETE FROM <table> t WHERE NOT EXISTS (
--       SELECT 1 FROM instruments i WHERE i.instrument_id = t.instrument_id);
--
-- No ON DELETE action is declared: instruments are retired by setting obsoleted_date, never
-- by DELETE, so a hard delete of an instrument that still owns rows should be refused
-- rather than quietly cascade into its financial history.

DO $$
DECLARE
    tbl text;
    referencing_tables text[] := ARRAY[
        'instrument_events',
        'instrument_reports',
        'instrument_prices',
        'processed_instrument_reports',
        'raw_instrument_processing_state'
    ];
    constraint_name text;
BEGIN
    FOREACH tbl IN ARRAY referencing_tables LOOP
        constraint_name := 'fk_' || tbl || '_instrument';

        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = constraint_name
              AND conrelid = tbl::regclass
        ) THEN
            EXECUTE format(
                'ALTER TABLE %I ADD CONSTRAINT %I FOREIGN KEY (instrument_id) REFERENCES instruments (instrument_id)',
                tbl, constraint_name);
        END IF;
    END LOOP;
END $$;
