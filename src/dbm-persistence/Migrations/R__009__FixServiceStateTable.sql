-- service_state (R__004) was designed as a history table: each change inserted a row and a trigger marked
-- the service's previous row is_current = FALSE. It never worked:
--   * the trigger fired on UPDATE as well as INSERT, and its own UPDATE re-fired it, so any second write
--     for a service failed with "stack depth limit exceeded";
--   * the primary key (service_name, is_current) only allowed one non-current row anyway;
--   * PersistCommonServiceState issued a plain UPDATE, which changes nothing when there is no row, so no
--     pause/resume was ever saved and a paused service resumed on restart.
--
-- Replace it with one row per service, written by upsert (UpdateCommonServiceStateStmt).
-- Idempotent: safe to run on a database created with the R__004 design or with this one.
--
-- Do not edit R__004: changing it would re-run it and re-create the broken trigger after this script.

DROP TRIGGER IF EXISTS update_last_modified_and_is_current ON service_state;
DROP FUNCTION IF EXISTS update_last_modified_and_is_current();

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = current_schema()
                 AND table_name = 'service_state'
                 AND column_name = 'is_current') THEN
        DELETE FROM service_state WHERE NOT is_current;
        ALTER TABLE service_state DROP CONSTRAINT IF EXISTS service_state_pkey;
        ALTER TABLE service_state DROP COLUMN is_current;
        ALTER TABLE service_state ADD PRIMARY KEY (service_name);
    END IF;
END $$;

CREATE OR REPLACE FUNCTION service_state_set_last_modified()
RETURNS TRIGGER AS $$
BEGIN
    NEW.last_modified = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS service_state_set_last_modified ON service_state;
CREATE TRIGGER service_state_set_last_modified
BEFORE INSERT OR UPDATE ON service_state
FOR EACH ROW
EXECUTE FUNCTION service_state_set_last_modified();
