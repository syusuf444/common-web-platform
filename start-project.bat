@echo off
setlocal

cd /d "%~dp0"
set "DOTNET_CLI_HOME=%~dp0.dotnet-cli"

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

if not exist "src\Platform.Api\Platform.Api.csproj" (
    echo Backend project was not found at src\Platform.Api\Platform.Api.csproj.
    echo Complete the backend setup before starting the project.
    exit /b 1
)

where.exe sqlcmd >nul 2>&1
if errorlevel 1 (
    echo SQLCMD was not found. Install SQL Server command-line tools and a SQL Server Database Engine.
    echo SSMS is only a management client; it does not install or run the Database Engine.
    exit /b 1
)

set "SQL_SERVER=%CWP_SQL_SERVER%"
if not defined SQL_SERVER set "SQL_SERVER=(localdb)\MSSQLLocalDB"

sqlcmd -S "%SQL_SERVER%" -E -Q "SET NOCOUNT ON; SELECT 1" -b -l 5 >nul 2>&1
if errorlevel 1 (
    echo Could not connect to SQL Server Database Engine at "%SQL_SERVER%" using Windows authentication.
    echo Install and start the SQL Server Database Engine, then confirm the instance in SSMS.
    echo For a named instance, set CWP_SQL_SERVER to its name, for example .\SQLEXPRESS.
    echo Keep SQL credentials in local secure configuration, never in this batch file.
    exit /b 1
)

sqlcmd -S "%SQL_SERVER%" -E -Q "SET NOCOUNT ON; IF DB_ID('CommonWebPlatform') IS NULL THROW 51000, 'Database is missing', 1" -b -l 5 >nul 2>&1
if errorlevel 1 (
    echo The CommonWebPlatform database is missing on "%SQL_SERVER%".
    echo Apply the local EF Core migration with:
    echo dotnet ef database update --project src\Platform.Api\Platform.Api.csproj --startup-project src\Platform.Api\Platform.Api.csproj
    exit /b 1
)

echo Starting the backend API. Swagger UI is available at the local URL shown by ASP.NET, followed by /swagger.
start "Common Web Platform API" cmd /k "cd /d ""%~dp0src\Platform.Api"" && set ASPNETCORE_ENVIRONMENT=Development && dotnet run"
exit /b 0
