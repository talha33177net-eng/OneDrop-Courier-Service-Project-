<#
.SYNOPSIS
    Builds and deploys the database: dbup pre -> dacpac publish -> dbup.

.DESCRIPTION
    The same three steps a pipeline runs, in the same order:
      1. dbup pre   Scripts/Pre (intentional data loss) against the old schema; creates the database if missing
      2. publish    src/Database dacpac via SqlPackage and Local.publish.xml
      3. dbup       seed and data migrations
    The connection string defaults to ConnectionStrings:Database in src/Web/appsettings.Local.json (git-ignored).

.EXAMPLE
    ./tools/db/publish.ps1
.EXAMPLE
    ./tools/db/publish.ps1 -Database OneDrop-Test      # same server, another database (integration tests)
.EXAMPLE
    ./tools/db/publish.ps1 -ScriptOnly                  # write the publish script to review, change nothing
#>
param(
    [string] $ConnectionString,
    [string] $Database,
    [switch] $ScriptOnly
)

# Native tools write progress to stderr; failures are detected from $LASTEXITCODE below instead
$ErrorActionPreference = 'Continue'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')

if (-not $ConnectionString) {
    # Git-ignored: the connection string (with its password) never goes into the repository
    $settings = Join-Path $root 'src\Web\appsettings.Local.json'
    if (-not (Test-Path $settings)) {
        throw "Missing $settings. Create it with { ""ConnectionStrings"": { ""Database"": ""Server=...;Database=OneDrop;..."" } } (see README)."
    }
    $ConnectionString = (Get-Content $settings -Raw | ConvertFrom-Json).ConnectionStrings.Database
}

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
if ($Database) {
    $builder['Database'] = $Database
}
$ConnectionString = $builder.ConnectionString
Write-Host "Target: $($builder['Data Source']) / $($builder['Database'])" -ForegroundColor Cyan

$project = Join-Path $root 'src\Database\Database.sqlproj'
$dacpac = Join-Path $root 'src\Database\bin\Debug\Database.dacpac'
$profile = Join-Path $root 'src\Database\Local.publish.xml'
$dbup = Join-Path $root 'src\Database Update\Database Update.csproj'

Write-Host 'Building the database project...' -ForegroundColor Cyan
dotnet build $project --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Database project build failed.' }

if ($ScriptOnly) {
    $output = Join-Path $root 'src\Database\bin\Debug\Database.publish.sql'
    sqlpackage /Action:Script /SourceFile:$dacpac /Profile:$profile /TargetConnectionString:$ConnectionString /OutputPath:$output
    if ($LASTEXITCODE -ne 0) { throw 'Script generation failed.' }
    Write-Host "Publish script written to $output" -ForegroundColor Green
    return
}

$env:ConnectionStrings__Database = $ConnectionString
try {
    Write-Host 'dbup pre...' -ForegroundColor Cyan
    dotnet run --project $dbup --nologo -- pre
    if ($LASTEXITCODE -ne 0) { throw 'dbup pre failed.' }

    Write-Host 'Publishing the dacpac...' -ForegroundColor Cyan
    sqlpackage /Action:Publish /SourceFile:$dacpac /Profile:$profile /TargetConnectionString:$ConnectionString
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Write-Host 'dbup...' -ForegroundColor Cyan
    dotnet run --project $dbup --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dbup failed.' }
}
finally {
    Remove-Item Env:\ConnectionStrings__Database -ErrorAction SilentlyContinue
}

Write-Host 'Database is up to date.' -ForegroundColor Green
