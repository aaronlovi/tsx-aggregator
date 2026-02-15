-- Add the column, and make it some time clearly in the past
ALTER TABLE state_fsm_state
    ADD next_fetch_stock_quote_time TIMESTAMPTZ NOT NULL
    DEFAULT '2023-01-01';
