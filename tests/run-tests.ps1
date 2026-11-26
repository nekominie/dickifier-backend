$ErrorActionPreference = 'Stop'
$composeFile = Join-Path $PSScriptRoot 'docker-compose.test.yml'

Write-Host "Starting disposable PostgreSQL test container..."
try {
    docker compose -f $composeFile up -d --wait
    if ($LASTEXITCODE -ne 0) { throw "Failed to start disposable PostgreSQL container." }

    $env:DICKIFIER_TEST_MIGRATOR_CONN = "Host=127.0.0.1;Port=15432;Database=dickifier_test;Username=dickifier_migrator;Password=test_migrator_password_only"
    $env:DICKIFIER_TEST_APP_CONN = "Host=127.0.0.1;Port=15432;Database=dickifier_test;Username=dickifier_app;Password=test_app_password_only"

    Write-Host "Running backend-dickifier.Tests against disposable PostgreSQL..."
    dotnet test (Join-Path $PSScriptRoot 'backend-dickifier.Tests.csproj') --configuration Release --verbosity normal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Write-Host "Tearing down disposable PostgreSQL test container..."
    docker compose -f $composeFile down -v --remove-orphans
}
