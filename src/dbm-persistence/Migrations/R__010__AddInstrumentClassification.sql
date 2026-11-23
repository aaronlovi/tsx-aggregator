-- TMX's sector and industry for each instrument, from the quote the RawCollector already downloads
-- (getEnhancedQuotes.json: classification.sector / classification.industry). Used to tell banks, insurers and
-- investment companies apart, whose cash flows the owner-earnings checks cannot read (scoring V5).
ALTER TABLE instruments
    ADD COLUMN IF NOT EXISTS sector varchar(100) NULL,
    ADD COLUMN IF NOT EXISTS industry varchar(100) NULL;
