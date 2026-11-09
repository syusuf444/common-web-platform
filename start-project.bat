@echo off
setlocal

cd /d "%~dp0"
set "DOTNET_CLI_HOME=%~dp0.dotnet-cli"
set "LOCALDB_INSTANCE=MSSQLLocalDB"
set "SQL_SERVER=%CWP_SQL_SERVER%"
if not defined SQL_SERVER set "SQL_SERVER=(localdb)\%LOCALDB_INSTANCE%"

where.exe dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET 10 SDK was not found. Install the .NET 10 SDK, then run this script again.
    exit /b 1
)

dotnet --list-sdks | findstr /B /C:"10." >nul
if errorlevel 1 (
    echo A .NET 10 SDK is required. Install the .NET 10 SDK, then run this script again.
    exit /b 1
)

where.exe npm >nul 2>&1
if errorlevel 1 (
    echo npm was not found. Install Node.js LTS, then run this script again.
    exit /b 1
)

if not exist "src\Platform.Api\Platform.Api.csproj" (
    echo Backend project was not found at src\Platform.Api\Platform.Api.csproj.
    echo Complete the backend setup before starting the project.
    exit /b 1
)

if not exist "frontend-evaluation\react\package.json" (
    echo React frontend was not found at frontend-evaluation\react.
    exit /b 1
)

if not exist "frontend-evaluation\react\package-lock.json" (
    echo React package lock file was not found. Restore the frontend project files before starting.
    exit /b 1
)

where.exe sqlcmd >nul 2>&1
if errorlevel 1 (
    echo SQLCMD was not found. Install SQL Server command-line tools and a SQL Server Database Engine.
    echo SSMS is only a management client; it does not install or run the Database Engine.
    exit /b 1
)

if not defined CWP_SQL_SERVER (
    where.exe sqllocaldb >nul 2>&1
    if errorlevel 1 (
        echo SQL Server LocalDB was not found. Install SQL Server Express LocalDB, then run this script again.
        exit /b 1
    )

    sqllocaldb info "%LOCALDB_INSTANCE%" >nul 2>&1
    if errorlevel 1 (
        echo Creating the SQL Server LocalDB instance "%LOCALDB_INSTANCE%".
        sqllocaldb create "%LOCALDB_INSTANCE%"
        if errorlevel 1 (
            echo Could not create the LocalDB instance "%LOCALDB_INSTANCE%".
            exit /b 1
        )
    )

    echo Starting SQL Server LocalDB instance "%LOCALDB_INSTANCE%".
    sqllocaldb start "%LOCALDB_INSTANCE%" >nul
    if errorlevel 1 (
        echo Could not start SQL Server LocalDB instance "%LOCALDB_INSTANCE%".
        exit /b 1
    )
)

echo Checking SQL Server at "%SQL_SERVER%".
sqlcmd -S "%SQL_SERVER%" -E -Q "SET NOCOUNT ON; SELECT 1" -b -l 5 >nul 2>&1
if errorlevel 1 (
    echo Could not connect to SQL Server Database Engine at "%SQL_SERVER%" using Windows authentication.
    echo Install and start the SQL Server Database Engine, then confirm the instance in SSMS.
    echo For a named instance, set CWP_SQL_SERVER to its name, for example .\SQLEXPRESS.
    echo Keep SQL credentials in local secure configuration, never in this batch file.
    exit /b 1
)

where.exe dotnet-ef >nul 2>&1
if errorlevel 1 (
    dotnet ef --version >nul 2>&1
    if errorlevel 1 (
        echo EF Core CLI was not found. Install it with: dotnet tool install --global dotnet-ef
        exit /b 1
    )
)

echo Creating or updating the CommonWebPlatform database.
dotnet ef database update --project "src\Platform.Api\Platform.Api.csproj" --startup-project "src\Platform.Api\Platform.Api.csproj"
if errorlevel 1 (
    echo Database migration failed. No application services were started.
    exit /b 1
)

if not exist "frontend-evaluation\react\node_modules" (
    echo Installing React frontend dependencies.
    pushd "frontend-evaluation\react"
    call npm ci
    if errorlevel 1 (
        popd
        echo Frontend dependency installation failed. No application services were started.
        exit /b 1
    )
    popd
)

echo Starting the backend API at http://localhost:5247.
start "Common Web Platform API" cmd /k "cd /d ""%~dp0src\Platform.Api"" && set ASPNETCORE_ENVIRONMENT=Development && dotnet run --launch-profile http"
if errorlevel 1 (
    echo Could not start the backend API console.
    exit /b 1
)

echo Starting the React frontend at http://localhost:5173.
start "Common Web Platform React" cmd /k "cd /d ""%~dp0frontend-evaluation\react"" && npm run dev -- --host localhost --port 5173 --strictPort"
if errorlevel 1 (
    echo Could not start the React frontend console.
    exit /b 1
)

echo.
echo Common Web Platform is starting:
echo   Frontend:  http://localhost:5173
echo   API:       http://localhost:5247
echo   Swagger:   http://localhost:5247/swagger
echo   Database server: %SQL_SERVER%
exit /b 0
