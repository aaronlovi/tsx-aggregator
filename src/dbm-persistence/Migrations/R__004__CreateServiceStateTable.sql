CREATE TABLE IF NOT EXISTS service_state (
    service_name VARCHAR(100) NOT NULL,
    is_paused BOOLEAN NOT NULL,
    last_modified TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_current BOOLEAN NOT NULL DEFAULT TRUE,
    PRIMARY KEY (service_name, is_current)
);

-- Trigger function to update the last_modified and is_current columns
CREATE OR REPLACE FUNCTION update_last_modified_and_is_current()
RETURNS TRIGGER AS $$
BEGIN
    -- Set all previous records for the same service to is_current = FALSE
    UPDATE service_state
    SET is_current = FALSE
    WHERE service_name = NEW.service_name AND is_current = TRUE;

    -- Update the last_modified timestamp and set is_current to TRUE for the new record
    NEW.last_modified = CURRENT_TIMESTAMP;
    NEW.is_current = TRUE;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Trigger to call the function on insert or update
CREATE TRIGGER update_last_modified_and_is_current
BEFORE INSERT OR UPDATE ON service_state
FOR EACH ROW
EXECUTE FUNCTION update_last_modified_and_is_current();
