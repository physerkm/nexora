-- Step 1: guard against double import and create the staging table for the raw CSV.
\set ON_ERROR_STOP on

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM tenants WHERE slug IN ('acme', 'globex') HAVING count(*) = 2) THEN
        RAISE EXCEPTION 'Demo tenants acme and globex are missing. Start the API once in Development so the demo data is seeded.';
    END IF;
    IF EXISTS (SELECT 1 FROM transactions WHERE reference LIKE 'TRX-%-TF%') THEN
        RAISE EXCEPTION 'TabFormer data is already imported. Run with -Reset to remove it first.';
    END IF;
END $$;

DROP TABLE IF EXISTS tabformer_raw;

-- UNLOGGED: no WAL for throw-away data, which makes the 24M row COPY much faster.
CREATE UNLOGGED TABLE tabformer_raw (
    user_id        int,
    card           int,
    year           int,
    month          int,
    day            int,
    "time"         text,
    amount         text,
    use_chip       text,
    merchant_name  text,
    merchant_city  text,
    merchant_state text,
    zip            text,
    mcc            int,
    errors         text,
    is_fraud       text
);
