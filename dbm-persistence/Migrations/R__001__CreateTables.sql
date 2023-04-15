-- create function now_utc() returns timestamp as $$
--   select now() at time zone 'utc';
-- $$ language sql;

CREATE TABLE if not exists processed_instrument_reports (
	instrument_id BIGINT NOT NULL,
	report_json TEXT NOT NULL,
	created_date TIMESTAMPTZ NOT NULL,
	obsoleted_date TIMESTAMPTZ
);
CREATE INDEX if not exists idx_processed_report ON processed_instrument_reports (instrument_id, obsoleted_date);

create table if not exists instruments (
    instrument_id bigint not null,
    exchange varchar(10) not null,
    company_symbol varchar(10) not null,
    company_name varchar(100) not null,
    instrument_symbol varchar(10) not null,
    instrument_name varchar(100) not null,
    created_date timestamptz not null,
    obsoleted_date timestamptz default null,
    primary key (instrument_id),
    constraint uniq_instruments unique (instrument_id, exchange, company_symbol, instrument_symbol)
);

create table if not exists instrument_prices (
    instrument_id bigint not null,
    price_per_share numeric(15,4) not null,
    num_shares bigint not null,
    created_date timestamptz not null,
    obsoleted_date timestamptz default null
);
create index if not exists idx_instrument_prices on instrument_prices (instrument_id, created_date);

create table if not exists instrument_reports (
    instrument_report_id bigint not null,
    instrument_id bigint not null,
    report_type int not null,
    report_period_type int not null,
    report_json text not null,
    report_date timestamptz not null,
    created_date timestamptz not null,
    obsoleted_date timestamptz default null,
    is_current boolean not null,
    primary key (instrument_report_id)
);
create index if not exists idx_instrument_report on instrument_reports (instrument_id, report_type, report_period_type, report_date);

create table if not exists generator (
    last_reserved bigint not null
);
insert into generator (last_reserved) values (1);

create table if not exists instrument_events (
    instrument_id bigint not null,
    event_date timestamptz not null,
    event_type int not null,
    is_processed boolean not null
);
create index if not exists idx_instrument_events on instrument_events (instrument_id);

create table if not exists raw_instrument_processing_state (
    instrument_id bigint not null,
    start_date timestamptz not null,
    is_complete boolean not null
);
create index if not exists idx_raw_instrument_processing_state on raw_instrument_processing_state (instrument_id);

create table if not exists state_fsm_state (
    next_fetch_directory_time timestamptz not null,
    next_fetch_instrument_data_time timestamptz not null,
    prev_company_symbol varchar(10) not null,
    prev_instrument_symbol varchar(10) not null
);
insert into state_fsm_state (next_fetch_directory_time, next_fetch_instrument_data_time, prev_company_symbol, prev_instrument_symbol) 
values(now_utc(), now_utc(), '', '');
