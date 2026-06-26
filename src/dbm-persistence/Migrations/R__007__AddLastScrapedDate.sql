-- Track the last time the RawCollector successfully scraped data for each
-- instrument, regardless of whether new rows were inserted. The previous proxy
-- (MAX(instrument_reports.created_date)) only moved when financials changed,
-- which made instruments look stale even when the scraper was running fine.
ALTER TABLE instruments
    ADD COLUMN IF NOT EXISTS last_scraped_date timestamptz NULL;
