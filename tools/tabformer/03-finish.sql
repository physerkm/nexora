-- Step 3: rebuild the indexes dropped in step 2 (same names and definitions as the EF migrations)
-- and drop the staging table. Safe to run again: missing indexes are created, existing ones skipped.
-- \gexec runs each CREATE INDEX as its own statement, so every finished index is committed on its own
-- and an interrupted run only has to redo the index it was building.
\set ON_ERROR_STOP on
\timing on

SET maintenance_work_mem = '1GB';
SET max_parallel_maintenance_workers = 4;

SELECT to_regclass('public.tabformer_index_backup') IS NOT NULL AS has_backup \gset

\if :has_backup
SELECT b.indexdef
FROM tabformer_index_backup b
WHERE to_regclass('public.' || quote_ident(b.indexname)) IS NULL
ORDER BY b.indexname
\gexec
\endif

DROP TABLE IF EXISTS tabformer_index_backup;
DROP TABLE IF EXISTS tabformer_raw;
