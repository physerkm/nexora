// Empties the browser-test database before the API starts; the API then migrates and seeds it from scratch.
// Refuses any database whose name does not end in "_e2e", so it can never wipe the development data.
import pg from 'pg';
import { db } from './env.js';

if (!db.database.endsWith('_e2e')) {
  console.error(`Refusing to reset "${db.database}": the browser-test database name must end with "_e2e".`);
  process.exit(1);
}

async function ensureDatabase() {
  const admin = new pg.Client({ ...db, database: 'postgres' });
  await admin.connect();
  try {
    const { rowCount } = await admin.query('SELECT 1 FROM pg_database WHERE datname = $1', [db.database]);
    if (rowCount === 0) {
      await admin.query(`CREATE DATABASE "${db.database}"`);
      console.log(`Created database ${db.database}.`);
    }
  } finally {
    await admin.end();
  }
}

try {
  await ensureDatabase();
} catch (error) {
  console.error(`Database ${db.database} is missing and "${db.user}" cannot create it (${error.message}).`);
  console.error(`Create it once as a superuser:  CREATE DATABASE ${db.database} OWNER ${db.user};`);
  process.exit(1);
}

const client = new pg.Client(db);
await client.connect();
try {
  // public is owned by the database owner (PostgreSQL 15+), so the owner may drop and recreate it.
  await client.query('DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;');
  console.log(`Reset database ${db.database}.`);
} finally {
  await client.end();
}
