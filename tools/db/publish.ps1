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
# Paths are built with Path.Combine and the connection string with DbConnectionStringBuilder, so the script runs the
# same in Windows PowerShell 5.1 and in PowerShell 7 on Linux (the Docker database image and CI)
$root = (Resolve-Path ([System.IO.Path]::Combine($PSScriptRoot, '..', '..'))).Path

if (-not $ConnectionString) {
    # Git-ignored: the connection string (with its password) never goes into the repository
    $settings = [System.IO.Path]::Combine($root, 'src', 'Web', 'appsettings.Local.json')
    if (-not (Test-Path $settings)) {
        throw "Missing $settings. Create it with { ""ConnectionStrings"": { ""Database"": ""Server=...;Database=OneDrop;..."" } } (see README)."
    }
    $ConnectionString = (Get-Content $settings -Raw | ConvertFrom-Json).ConnectionStrings.Database
}

$builder = New-Object System.Data.Common.DbConnectionStringBuilder
$builder.set_ConnectionString($ConnectionString)  # set_ and get_: PowerShell reads .ConnectionString on a dictionary as a key
if ($Database) {
    $null = $builder.Remove('Initial Catalog')
    $builder['Database'] = $Database
}
$ConnectionString = $builder.get_ConnectionString()
$server = @('Server', 'Data Source') | Where-Object { $builder.ContainsKey($_) } | ForEach-Object { $builder[$_] } | Select-Object -First 1
$name = @('Database', 'Initial Catalog') | Where-Object { $builder.ContainsKey($_) } | ForEach-Object { $builder[$_] } | Select-Object -First 1
Write-Host "Target: $server / $name" -ForegroundColor Cyan

$project = [System.IO.Path]::Combine($root, 'src', 'Database', 'Database.sqlproj')
$dacpac = [System.IO.Path]::Combine($root, 'src', 'Database', 'bin', 'Debug', 'Database.dacpac')
$dbup = [System.IO.Path]::Combine($root, 'src', 'Database Update', 'Database Update.csproj')

# The publish settings live here, not in a profile: *.publish.xml is git-ignored, so a profile would be missing on a
# fresh checkout (CI). Rebuilding a table under Row-Level Security is safe: the policy restricts only connections the
# application marks, so SqlPackage's own connection copies every row.
$options = @(
    '/p:IncludeCompositeObjects=True',
    '/p:AllowIncompatiblePlatform=True',
    '/p:BlockOnPossibleDataLoss=True',
    '/p:DropObjectsNotInSource=False',
    '/p:AllowUnsafeRowLevelSecurityDataMovement=True'
)

Write-Host 'Building the database project...' -ForegroundColor Cyan
dotnet build $project --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Database project build failed.' }

if ($ScriptOnly) {
    $output = [System.IO.Path]::Combine($root, 'src', 'Database', 'bin', 'Debug', 'Database.publish.sql')
    sqlpackage /Action:Script /SourceFile:$dacpac @options /TargetConnectionString:$ConnectionString /OutputPath:$output
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
    sqlpackage /Action:Publish /SourceFile:$dacpac @options /TargetConnectionString:$ConnectionString
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Write-Host 'dbup...' -ForegroundColor Cyan
    dotnet run --project $dbup --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dbup failed.' }
}
finally {
    Remove-Item Env:\ConnectionStrings__Database -ErrorAction SilentlyContinue
}

Write-Host 'Database is up to date.' -ForegroundColor Green
