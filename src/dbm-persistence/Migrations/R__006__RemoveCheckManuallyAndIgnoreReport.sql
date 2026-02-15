-- Merge check_manually reports into their corresponding current reports,
-- then remove the check_manually and ignore_report columns.
-- Guarded with column existence checks so this is safe to re-run.

DO $$
BEGIN
    -- Only run merge/delete if the columns still exist
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'instrument_reports' AND column_name = 'check_manually'
    ) THEN
        -- Merge check_manually reports into their corresponding current reports.
        -- For each check_manually=true row, find the matching current report
        -- (same instrument, report type, period type, report date) and merge
        -- the JSON: new/changed keys override, existing-only keys are preserved.
        UPDATE instrument_reports AS curr
        SET report_json = (
            SELECT (curr.report_json::jsonb || cm.report_json::jsonb)::text
            FROM instrument_reports AS cm
            WHERE cm.check_manually = true
              AND cm.instrument_id = curr.instrument_id
              AND cm.report_type = curr.report_type
              AND cm.report_period_type = curr.report_period_type
              AND cm.report_date = curr.report_date
            ORDER BY cm.created_date DESC
            LIMIT 1
        )
        WHERE curr.is_current = true
          AND curr.check_manually = false
          AND EXISTS (
            SELECT 1 FROM instrument_reports cm
            WHERE cm.check_manually = true
              AND cm.instrument_id = curr.instrument_id
              AND cm.report_type = curr.report_type
              AND cm.report_period_type = curr.report_period_type
              AND cm.report_date = curr.report_date
        );

        -- Delete rows that were pending manual review (now merged)
        DELETE FROM instrument_reports WHERE check_manually = true;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'instrument_reports' AND column_name = 'ignore_report'
    ) THEN
        -- Delete rows that were explicitly ignored
        DELETE FROM instrument_reports WHERE ignore_report = true;
    END IF;
END $$;

-- Drop the columns (IF EXISTS makes this idempotent)
ALTER TABLE instrument_reports DROP COLUMN IF EXISTS check_manually;
ALTER TABLE instrument_reports DROP COLUMN IF EXISTS ignore_report;
