import { Injectable } from '@angular/core';

@Injectable({
    providedIn: 'root'
})
export class TextService {

    constructor() { }

    ///////////////////////////////////////////////////////////////////////////
    // General text strings
    text_avg_net_cash_flow: string = "text_avg_net_cash_flow";
    text_avg_owner_earnings: string = "text_avg_owner_earnings";
    text_book_value: string = "text_book_value";
    text_companies: string = "text_companies";
    text_company_name: string = "text_company_name";
    text_company_symbol: string = "text_company_symbol";
    text_cur_dividends_paid: string = "text_cur_dividends_paid";
    text_cur_price_to_book_ratio: string = "text_cur_price_to_book_ratio";
    text_cur_retained_earnings: string = "text_cur_retained_earnings";
    text_debt_to_book_ratio: string = "text_debt_to_book_ratio";
    text_debt_to_equity_ratio: string = "text_debt_to_equity_ratio";
    text_exchange: string = "text_exchange";
    text_financials: string = "text_financials";
    text_home: string = "text_home";
    text_instrument_name: string = "text_instrument_name";
    text_instrument_symbol: string = "text_instrument_symbol";
    text_loading_ellipses: string = "text_loading_ellipses";
    text_long_term_debt: string = "text_long_term_debt";
    text_market_cap: string = "text_market_cap";
    text_max_price: string = "text_max_price";
    text_percentage_upside: string = "text_percentage_upside";
    text_no: string = "text_no";
    text_number_of_shares: string = "text_number_of_shares";
    text_oldest_retained_earnings: string = "text_oldest_retained_earnings";
    text_overall_score: string = "text_overall_score";
    text_overview: string = "text_overview";
    text_price_per_share: string = "text_price_per_share";
    text_scores: string = "text_scores";
    text_total_shareholders_equity: string = "text_total_shareholders_equity";
    text_yes: string = "text_yes";

    ///////////////////////////////////////////////////////////////////////////
    // About component strings
    about_title: string = 'about_title';
    about_welcome: string = 'about_welcome';
    about_description1: string = 'about_description1';
    about_description2: string = 'about_description2';
    about_description3: string = 'about_description3';
    about_description4: string = 'about_description4';
    about_description5: string = 'about_description5';
    about_description6: string = 'about_description6';

    ///////////////////////////////////////////////////////////////////////////
    // Company details component strings
    company_details_debt_to_equity_small_enough_tooltip: string = "company_details_debt_to_equity_small_enough_tooltip";
    company_details_debt_to_equity_small_enough: string = "company_details_debt_to_equity_small_enough";
    company_details_debt_to_book_small_enough_tooltip: string = "company_details_debt_to_book_small_enough_tooltip";
    company_details_debt_to_book_small_enough: string = "company_details_debt_to_book_small_enough";
    company_details_book_value_big_enough_tooltip: string = "company_details_book_value_big_enough_tooltip";
    company_details_book_value_big_enough: string = "company_details_book_value_big_enough";
    company_details_price_to_book_small_enough_tooltip: string = "company_details_price_to_book_small_enough_tooltip";
    company_details_price_to_book_small_enough: string = "company_details_price_to_book_small_enough";
    company_details_est_next_year_total_return_cash_flow_big_enough_tooltip: string = "company_details_est_next_year_total_return_cash_flow_big_enough_tooltip";
    company_details_est_next_year_total_return_cash_flow_big_enough: string = "company_details_est_next_year_total_return_cash_flow_big_enough";
    company_details_est_next_year_total_return_owner_earnings_big_enough_tooltip: string = "company_details_est_next_year_total_return_owner_earnings_big_enough_tooltip";
    company_details_est_next_year_total_return_owner_earnings_big_enough: string = "company_details_est_next_year_total_return_owner_earnings_big_enough";
    company_details_est_next_year_total_return_cash_flow_not_too_big_tooltip: string = "company_details_est_next_year_total_return_cash_flow_not_too_big_tooltip";
    company_details_est_next_year_total_return_cash_flow_not_too_big: string = "company_details_est_next_year_total_return_cash_flow_not_too_big";
    company_details_est_next_year_total_return_owner_earnings_not_too_big_tooltip: string = "company_details_est_next_year_total_return_owner_earnings_not_too_big_tooltip";
    company_details_est_next_year_total_return_owner_earnings_not_too_big: string = "company_details_est_next_year_total_return_owner_earnings_not_too_big";
    company_details_avg_cash_flow_positive_tooltip: string = "company_details_avg_cash_flow_positive_tooltip";
    company_details_avg_cash_flow_positive: string = "company_details_avg_cash_flow_positive";
    company_details_avg_owner_earnings_positive_tooltip: string = "company_details_avg_owner_earnings_positive_tooltip";
    company_details_avg_owner_earnings_positive: string = "company_details_avg_owner_earnings_positive";
    company_details_retained_earnings_positive_tooltip: string = "company_details_retained_earnings_positive_tooltip";
    company_details_retained_earnings_positive: string = "company_details_retained_earnings_positive";
    company_details_retained_earnings_increased_tooltip: string = "company_details_retained_earnings_increased_tooltip";
    company_details_retained_earnings_increased: string = "company_details_retained_earnings_increased";
    company_details_history_long_enough_tooltip: string = "company_details_history_long_enough_tooltip";
    company_details_history_long_enough: string = "company_details_history_long_enough";
    company_details_max_price_tooltip: string = "company_details_max_price_tooltip";
    company_details_market_cap_tooltip: string = "company_details_market_cap_tooltip";
    company_details_long_term_debt_tooltip: string = "company_details_long_term_debt_tooltip";
    company_details_total_shareholders_equity_tooltip: string = "company_details_total_shareholders_equity_tooltip";
    company_details_cur_dividends_paid_tooltip: string = "company_details_cur_dividends_paid_tooltip";
    company_details_avg_net_cash_flow_tooltip: string = "company_details_avg_net_cash_flow_tooltip";
    company_details_avg_owner_earnings_tooltip: string = "company_details_avg_owner_earnings_tooltip";
    company_details_cur_retained_earnings_tooltip: string = "company_details_cur_retained_earnings_tooltip";
    company_details_oldest_retained_earnings_tooltip: string = "company_details_oldest_retained_earnings_tooltip";
    company_details_debt_to_equity_ratio_tooltip: string = "company_details_debt_to_equity_ratio_tooltip";
    company_details_cur_price_to_book_ratio_tooltip: string = "company_details_cur_price_to_book_ratio_tooltip";
    company_details_debt_to_book_ratio_tooltip: string = "company_details_debt_to_book_ratio_tooltip";
    company_details_book_value_tooltip: string = "company_details_book_value_tooltip";
    company_details_est_next_year_book_value_from_cash_flow_tooltip: string = "company_details_est_next_year_book_value_from_cash_flow_tooltip";
    company_details_est_next_year_book_value_from_cash_flow: string = "company_details_est_next_year_book_value_from_cash_flow";
    company_details_est_next_year_book_value_from_owner_earnings: string = "company_details_est_next_year_book_value_from_owner_earnings";
    company_details_est_next_year_book_value_from_owner_earnings_tooltip: string = "company_details_est_next_year_book_value_from_owner_earnings_tooltip";
    company_details_est_next_year_total_return_from_cash_flow_tooltip: string = "company_details_est_next_year_total_return_from_cash_flow_tooltip";
    company_details_est_next_year_total_return_from_cash_flow: string = "company_details_est_next_year_total_return_from_cash_flow";
    company_details_est_next_year_total_return_from_owner_earnings_tooltip: string = "company_details_est_next_year_total_return_from_owner_earnings_tooltip";
    company_details_est_next_year_total_return_from_owner_earnings: string = "company_details_est_next_year_total_return_from_owner_earnings";
    company_details_num_annual_reports_tooltip: string = "company_details_num_annual_reports_tooltip";
    company_details_num_annual_reports: string = "company_details_num_annual_reports";

    ///////////////////////////////////////////////////////////////////////////
    // Company list component strings
    company_list_get_top_30_companies: string = "company_list_get_top_30_companies";
    company_list_get_bottom_30_companies: string = "company_list_get_bottom_30_companies";
    company_list_est_next_year_total_return_from_cash_flow: string = "company_list_est_next_year_total_return_from_cash_flow";
    company_list_est_next_year_total_return_from_owner_earnings: string = "company_list_est_next_year_total_return_from_owner_earnings";

    ///////////////////////////////////////////////////////////////////////////
    // Header component strings
    header_tsx_deep_value_guide: string = "header_tsx_deep_value_guide";

    ///////////////////////////////////////////////////////////////////////////
    // System controls component strings
    system_controls_title: string = "system_controls_title";
    system_controls_priority_heading: string = "system_controls_priority_heading";
    system_controls_input_label: string = "system_controls_input_label";
    system_controls_set_button: string = "system_controls_set_button";
    system_controls_clear_button: string = "system_controls_clear_button";
    system_controls_queue_empty: string = "system_controls_queue_empty";
    system_controls_success: string = "system_controls_success";
    system_controls_error: string = "system_controls_error";
}
