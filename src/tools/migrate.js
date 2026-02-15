import aws from 'aws-sdk';
import pkg from 'pg';

const { config, DynamoDB } = aws;
const { Client } = pkg;

// Initialize AWS SDK
// I am using us-east-2 (Ohio) as my region
// Additional credentials are configured using the AWS CLI,
// see https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-quickstart.html
config.update({ region: 'us-east-2' });
const dynamoDB = new DynamoDB.DocumentClient();

// Connect to Postgres
// My local credentials will not help you :)
// You need to create a connection to your own local database...
const postgresClient = new Client({
    user: 'postgres',
    host: '127.0.0.1',
    database: 'tsx-scraper',
    password: 'postgres',
    port: '5432',
});

const postgresSql = 'SELECT i.instrument_id,'
    + ' i.exchange,'
    + ' i.company_symbol,'
    + ' i.company_name,'
    + ' i.instrument_symbol,'
    + ' i.instrument_name,'
    + ' ip.price_per_share,'
    + ' pir.report_json'
    + ' FROM instruments i'
    + ' JOIN instrument_prices ip ON i.instrument_id = ip.instrument_id AND ip.obsoleted_date IS NULL'
    + ' JOIN processed_instrument_reports pir ON i.instrument_id = pir.instrument_id AND pir.obsoleted_date IS NULL'
    + ' WHERE i.obsoleted_date IS null';

try {
    await postgresClient.connect();

    const result = await postgresClient.query(postgresSql);

    for (let row of result.rows) {
        const instrumentReportJson = JSON.parse(row.report_json);
        const instrumentId = Number(row.instrument_id);

        if (isNaN(instrumentId) || instrumentId === 0 || instrumentId === undefined) {
            console.error(`Instrument ID is not a number or is 0 or undefined: ${instrumentId}, row.instrument_id: ${row.instrument_id}`);
            break; // Abort early to inspect the error
        }

        // Generate the DynamoDB object
        const params = {
            TableName: 'Instruments',
            Item: {
                instrument_id: Number(row.instrument_id),
                exchange: row.exchange,
                company_symbol: row.company_symbol,
                company_name: row.company_name,
                instrument_symbol: row.instrument_symbol,
                instrument_name: row.instrument_name,
                price_per_share: row.price_per_share,
                instrument_report: instrumentReportJson
            },
        };

        dynamoDB.put(params, (err, data) => {
            if (err) {
                console.error('Error inserting data:', err);
            } else {
                console.log('Data inserted successfully:', data);
            }
        });
    }
} catch (error) {
    console.error('Error fetching and processing data:', error);
}
finally {
    postgresClient.end();
}
