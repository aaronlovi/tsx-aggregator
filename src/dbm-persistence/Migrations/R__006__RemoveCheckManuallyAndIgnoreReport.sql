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

-- Delete rows that were pending manual review (now merged) or explicitly ignored
DELETE FROM instrument_reports WHERE check_manually = true;
DELETE FROM instrument_reports WHERE ignore_report = true;

-- Drop the columns
ALTER TABLE instrument_reports DROP COLUMN IF EXISTS check_manually;
ALTER TABLE instrument_reports DROP COLUMN IF EXISTS ignore_report;
