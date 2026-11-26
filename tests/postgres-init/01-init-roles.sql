CREATE ROLE dickifier_migrator LOGIN PASSWORD 'test_migrator_password_only' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE dickifier_app LOGIN PASSWORD 'test_app_password_only' NOSUPERUSER NOCREATEDB NOCREATEROLE;

GRANT CONNECT ON DATABASE dickifier_test TO dickifier_migrator, dickifier_app;
GRANT USAGE, CREATE ON SCHEMA public TO dickifier_migrator;
GRANT USAGE ON SCHEMA public TO dickifier_app;

ALTER DEFAULT PRIVILEGES FOR ROLE dickifier_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO dickifier_app;

ALTER DEFAULT PRIVILEGES FOR ROLE dickifier_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO dickifier_app;
