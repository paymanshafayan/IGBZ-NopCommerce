# build.ps1 — اسکریپت بیلد محلی ویندوزی برای پلاگین‌های IGBZ روی nopCommerce 4.90.6
param(
    [string]$NopDir = "..\nopCommerce-4.90.6"
)

$ErrorActionPreference = "Stop"
$NopVersion = "release-4.90.6"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==> بررسی dotnet SDK..." -ForegroundColor Cyan
dotnet --version

if (-not (Test-Path "$NopDir\src\NopCommerce.sln")) {
    Write-Host "==> کلون nopCommerce $NopVersion در $NopDir ..." -ForegroundColor Cyan
    if (Test-Path $NopDir) { Remove-Item -Recurse -Force $NopDir }
    git clone --depth 1 --branch $NopVersion https://github.com/nopSolutions/nopCommerce.git $NopDir
} else {
    Write-Host "==> استفاده از سورس موجود در $NopDir" -ForegroundColor Green
}

Write-Host "==> کپی پلاگین‌ها..." -ForegroundColor Cyan
Copy-Item -Recurse -Force "$ScriptDir\Plugins\Nop.Plugin.Api" "$NopDir\src\Plugins\"
Copy-Item -Recurse -Force "$ScriptDir\Plugins\Nop.Plugin.Misc.MultiTenantStores" "$NopDir\src\Plugins\"
Copy-Item -Recurse -Force "$ScriptDir\Plugins\Nop.Plugin.Misc.InstagramAssistant" "$NopDir\src\Plugins\"
Copy-Item -Recurse -Force "$ScriptDir\Plugins\Nop.Plugin.Misc.MasterSiteHub" "$NopDir\src\Plugins\"

Set-Location $NopDir

Write-Host "==> افزودن به sln ..." -ForegroundColor Cyan
dotnet sln src\NopCommerce.sln add src\Plugins\Nop.Plugin.Misc.MultiTenantStores\Nop.Plugin.Misc.MultiTenantStores.csproj --in-root 2>$null; $LASTEXITCODE = 0
dotnet sln src\NopCommerce.sln add src\Plugins\Nop.Plugin.Misc.InstagramAssistant\Nop.Plugin.Misc.InstagramAssistant.csproj --in-root 2>$null; $LASTEXITCODE = 0
dotnet sln src\NopCommerce.sln add src\Plugins\Nop.Plugin.Misc.MasterSiteHub\Nop.Plugin.Misc.MasterSiteHub.csproj --in-root 2>$null; $LASTEXITCODE = 0
dotnet sln src\NopCommerce.sln add src\Plugins\Nop.Plugin.Api\Nop.Plugin.Api.csproj --in-root 2>$null; $LASTEXITCODE = 0

Write-Host "==> Restore ..." -ForegroundColor Cyan
dotnet restore src\NopCommerce.sln

Write-Host "==> Build MultiTenantStores ..." -ForegroundColor Cyan
dotnet build src\Plugins\Nop.Plugin.Misc.MultiTenantStores\Nop.Plugin.Misc.MultiTenantStores.csproj -c Release --no-restore -v minimal

Write-Host "==> Build سایر پلاگین‌ها ..." -ForegroundColor Cyan
dotnet build src\Plugins\Nop.Plugin.Api\Nop.Plugin.Api.csproj -c Release --no-restore -v minimal
dotnet build src\Plugins\Nop.Plugin.Misc.MasterSiteHub\Nop.Plugin.Misc.MasterSiteHub.csproj -c Release --no-restore -v minimal
dotnet build src\Plugins\Nop.Plugin.Misc.InstagramAssistant\Nop.Plugin.Misc.InstagramAssistant.csproj -c Release --no-restore -v minimal

Write-Host "`n✅ بیلد موفق!" -ForegroundColor Green
Write-Host "خروجی: $NopDir\src\Presentation\Nop.Web\Plugins\"
Get-ChildItem "$NopDir\src\Presentation\Nop.Web\Plugins\Misc.MultiTenantStores\*.dll" | Select-Object Name, Length
