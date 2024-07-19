-- Add the column, and set the default to false for existing rows
ALTER TABLE instrument_reports
    ADD check_manually BOOLEAN NOT NULL
    DEFAULT false;
