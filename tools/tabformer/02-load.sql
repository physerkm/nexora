-- Step 2: map TabFormer rows onto Nexora tenants, branches and transactions.
--   card holders 0-999 -> acme, 1000-1999 -> globex
--   merchant state / country / ONLINE -> one branch per value and tenant
--   negative amount -> Refund, MCC 4829 -> Transfer, MCC 6011 (ATM) -> Payout, otherwise Payment
--   fraud or processing error -> Rejected, last 36 hours -> Pending, otherwise Approved
--   dates are shifted so the newest transaction falls on yesterday (relative spacing is preserved)
\set ON_ERROR_STOP on
\timing on

SET work_mem = '256MB';
SET maintenance_work_mem = '1GB';

-- Tenants and the users that create / review the imported transactions.
CREATE TEMP TABLE tf_tenant AS
SELECT t.id AS tenant_id,
       CASE t.slug WHEN 'acme' THEN 0 ELSE 1 END AS bucket,
       (SELECT u.id FROM users u JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id
         WHERE u.tenant_id = t.id AND r.name = 'TenantAdmin' ORDER BY u.created_at LIMIT 1) AS creator_id,
       (SELECT u.id FROM users u JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id
         WHERE u.tenant_id = t.id AND r.name = 'Manager' ORDER BY u.created_at LIMIT 1) AS reviewer_id
FROM tenants t
WHERE t.slug IN ('acme', 'globex');

DO $$
BEGIN
    IF (SELECT count(*) FROM tf_tenant WHERE creator_id IS NOT NULL AND reviewer_id IS NOT NULL) <> 2 THEN
        RAISE EXCEPTION 'Each demo tenant needs a TenantAdmin and a Manager user.';
    END IF;
END $$;

-- Readable counterparty names for the most common merchant category codes.
CREATE TEMP TABLE tf_mcc (mcc int PRIMARY KEY, label text);
INSERT INTO tf_mcc VALUES
    (5411, 'Market'), (5499, 'Gıda'), (5812, 'Restoran'), (5814, 'Fast food'), (5541, 'Akaryakıt'),
    (5912, 'Eczane'), (5300, 'Toptan satış'), (5311, 'Mağaza'), (5310, 'İndirim mağazası'), (4829, 'Para transferi'),
    (6011, 'ATM'), (4121, 'Taksi'), (4784, 'Otoyol'), (4900, 'Fatura'), (4814, 'Telekom'), (5815, 'Dijital içerik'),
    (7538, 'Oto servis'), (5651, 'Giyim'), (5942, 'Kitapçı'), (7832, 'Sinema'), (5719, 'Ev eşyası'),
    (5732, 'Elektronik'), (5970, 'Hobi'), (5211, 'Yapı market'), (8021, 'Diş hekimi'), (8099, 'Sağlık'),
    (7011, 'Otel'), (4511, 'Havayolu'), (5813, 'Bar'), (7349, 'Temizlik'), (5921, 'Tekel');

-- One branch per merchant state (US), country (foreign) or ONLINE, in each tenant.
CREATE TEMP TABLE tf_branch_key AS
SELECT DISTINCT
       CASE WHEN user_id < 1000 THEN 0 ELSE 1 END AS bucket,
       CASE WHEN merchant_city = 'ONLINE' THEN 'ONLINE'
            WHEN coalesce(merchant_state, '') = '' THEN 'UNKNOWN'
            ELSE merchant_state END AS state_key
FROM tabformer_raw;

CREATE TEMP TABLE tf_branch AS
SELECT k.bucket, k.state_key, tt.tenant_id,
       'TF-' || lpad(dense_rank() OVER (ORDER BY k.state_key)::text, 3, '0') AS code,
       CASE WHEN k.state_key = 'ONLINE' THEN 'Online satışlar'
            WHEN k.state_key = 'UNKNOWN' THEN 'Diğer'
            WHEN length(k.state_key) = 2 THEN 'ABD · ' || k.state_key
            ELSE left('Yurt dışı · ' || k.state_key, 100) END AS name
FROM tf_branch_key k
JOIN tf_tenant tt ON tt.bucket = k.bucket;

INSERT INTO branches (id, tenant_id, code, name, city, is_active, created_at)
SELECT uuidv7(), tenant_id, code, name, NULL, true, now()
FROM tf_branch
ON CONFLICT (tenant_id, code) DO NOTHING;

ALTER TABLE tf_branch ADD COLUMN branch_id uuid;
UPDATE tf_branch tb SET branch_id = b.id FROM branches b WHERE b.tenant_id = tb.tenant_id AND b.code = tb.code;
CREATE INDEX ON tf_branch (bucket, state_key);

-- The active fee rule per tenant and type, for USD (currency-specific rules win over generic ones).
CREATE TEMP TABLE tf_fee AS
SELECT DISTINCT ON (fr.tenant_id, fr.transaction_type)
       fr.tenant_id, fr.transaction_type, fr.percentage, fr.fixed_amount, fr.min_fee, fr.max_fee
FROM fee_rules fr
JOIN tf_tenant tt ON tt.tenant_id = fr.tenant_id
WHERE fr.is_active AND (fr.currency IS NULL OR fr.currency = 'USD')
ORDER BY fr.tenant_id, fr.transaction_type, (fr.currency IS NOT NULL) DESC;

-- Keep the index definitions, then drop the secondary indexes: maintaining 11 indexes row by row
-- during a 24M row insert is far slower than building them once afterwards (03-finish.sql).
CREATE TABLE IF NOT EXISTS tabformer_index_backup (indexname text PRIMARY KEY, indexdef text NOT NULL);
INSERT INTO tabformer_index_backup
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'transactions' AND indexname <> 'pk_transactions'
ON CONFLICT (indexname) DO NOTHING;

DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT indexname FROM tabformer_index_backup LOOP
        EXECUTE format('DROP INDEX IF EXISTS %I', r.indexname);
    END LOOP;
END $$;

CREATE TEMP SEQUENCE tf_seq;

INSERT INTO transactions (
    id, tenant_id, branch_id, reference, type, amount, currency, fee, net_amount, status,
    description, counterparty_name, status_reason, idempotency_key, created_by_user_id, created_at,
    reviewed_by_user_id, reviewed_at, cancelled_by_user_id, cancelled_at, updated_at)
SELECT
    uuidv7(),
    x.tenant_id,
    x.branch_id,
    'TRX-' || to_char(x.created_at AT TIME ZONE 'UTC', 'YYYYMMDD') || '-TF' || lpad(x.seq::text, 8, '0'),
    x.type,
    x.amount,
    'USD',
    x.fee,
    x.amount - x.fee,
    x.status,
    x.description,
    x.counterparty,
    x.reason,
    NULL,
    x.creator_id,
    x.created_at,
    CASE WHEN x.status <> 'Pending' THEN x.reviewer_id END,
    CASE WHEN x.status <> 'Pending' THEN least(x.created_at + make_interval(mins => 5 + (x.seq % 235)::int), now()) END,
    NULL,
    NULL,
    CASE WHEN x.status <> 'Pending' THEN least(x.created_at + make_interval(mins => 5 + (x.seq % 235)::int), now()) END
FROM (
    SELECT s.*,
           CASE WHEN f.tenant_id IS NULL THEN 0
                ELSE least(s.amount, round(least(greatest(s.amount * f.percentage / 100 + f.fixed_amount, f.min_fee),
                                                  coalesce(f.max_fee, s.amount)), 2)) END AS fee,
           CASE WHEN s.is_fraud = 'Yes' OR coalesce(s.errors, '') <> '' THEN 'Rejected'
                WHEN s.created_at > now() - interval '36 hours' THEN 'Pending'
                ELSE 'Approved' END AS status,
           CASE WHEN s.is_fraud = 'Yes' THEN 'Dolandırıcılık şüphesi'
                WHEN coalesce(s.errors, '') <> '' THEN left('İşlem hatası: ' || rtrim(s.errors, ','), 500) END AS reason
    FROM (
        SELECT nextval('tf_seq') AS seq,
               tt.tenant_id, tt.creator_id, tt.reviewer_id, tb.branch_id,
               abs(r.amt) AS amount,
               CASE WHEN r.amt < 0 THEN 'Refund' WHEN r.mcc = 4829 THEN 'Transfer' WHEN r.mcc = 6011 THEN 'Payout' ELSE 'Payment' END AS type,
               (make_timestamp(r.year, r.month, r.day, split_part(r."time", ':', 1)::int, split_part(r."time", ':', 2)::int, 0)
                   + shift.d) AT TIME ZONE 'UTC' AS created_at,
               left(coalesce(m.label, 'İşyeri') || ' · ' ||
                    CASE WHEN r.merchant_city = 'ONLINE' THEN 'Online' ELSE initcap(r.merchant_city) END, 200) AS counterparty,
               left('Kart ' || r.user_id || '/' || r.card || ' · ' || r.use_chip || ' · MCC ' || r.mcc, 500) AS description,
               r.errors, r.is_fraud
        FROM (SELECT *, replace(amount, '$', '')::numeric(18, 2) AS amt FROM tabformer_raw) r
        CROSS JOIN (
            SELECT (date_trunc('day', now() AT TIME ZONE 'UTC') - interval '1 day')
                   - max(make_timestamp(year, month, day, 0, 0, 0)) AS d
            FROM tabformer_raw) shift
        JOIN tf_tenant tt ON tt.bucket = CASE WHEN r.user_id < 1000 THEN 0 ELSE 1 END
        JOIN tf_branch tb ON tb.bucket = tt.bucket
                         AND tb.state_key = CASE WHEN r.merchant_city = 'ONLINE' THEN 'ONLINE'
                                                 WHEN coalesce(r.merchant_state, '') = '' THEN 'UNKNOWN'
                                                 ELSE r.merchant_state END
        LEFT JOIN tf_mcc m ON m.mcc = r.mcc
        WHERE r.amt <> 0
    ) s
    LEFT JOIN tf_fee f ON f.tenant_id = s.tenant_id AND f.transaction_type = s.type
) x;

-- One audit entry per tenant documents the bulk import (individual rows are not audited).
-- Counted from the staging table: the transactions table has no secondary indexes at this point.
INSERT INTO audit_logs (id, tenant_id, user_id, user_email, action, entity_type, entity_id, data, created_at)
SELECT uuidv7(), tt.tenant_id, NULL, 'system', 'import.tabformer', 'Tenant', tt.tenant_id::text,
       jsonb_build_object('source', 'IBM TabFormer card_transaction.v1.csv', 'license', 'Apache-2.0',
                          'transactions', c.n),
       now()
FROM tf_tenant tt
JOIN (SELECT CASE WHEN user_id < 1000 THEN 0 ELSE 1 END AS bucket, count(*) AS n
      FROM tabformer_raw
      WHERE replace(amount, '$', '')::numeric(18, 2) <> 0
      GROUP BY 1) c ON c.bucket = tt.bucket;
