#!/usr/bin/env bash
# build.sh — اسکریپت بیلد محلی برای پلاگین‌های IGBZ روی nopCommerce 4.90.6
# پیش‌نیاز: .NET SDK 9.0.x نصب باشد: dotnet --version
# استفاده:
#   ./build.sh               # کلون خودکار nopCommerce در ../nopCommerce-4.90.6 و بیلد
#   ./build.sh /path/to/nopCommerce  # اگر سورس nopCommerce را از قبل دارید

set -e

NOP_VERSION="release-4.90.6"
DEFAULT_NOP_DIR="../nopCommerce-4.90.6"
NOP_DIR="${1:-$DEFAULT_NOP_DIR}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> بررسی dotnet SDK..."
dotnet --version || (echo "dotnet SDK یافت نشد — لطفا .NET 9 SDK نصب کنید." && exit 1)

if [ ! -d "$NOP_DIR/src" ]; then
  echo "==> سورس nopCommerce در $NOP_DIR یافت نشد — در حال کلون release $NOP_VERSION ..."
  rm -rf "$NOP_DIR"
  git clone --depth 1 --branch "$NOP_VERSION" https://github.com/nopSolutions/nopCommerce.git "$NOP_DIR"
else
  echo "==> استفاده از سورس موجود در $NOP_DIR"
fi

echo "==> کپی پلاگین‌های IGBZ به $NOP_DIR/src/Plugins ..."
mkdir -p "$NOP_DIR/src/Plugins"
cp -r "$SCRIPT_DIR/Plugins/Nop.Plugin.Api" "$NOP_DIR/src/Plugins/"
cp -r "$SCRIPT_DIR/Plugins/Nop.Plugin.Misc.MultiTenantStores" "$NOP_DIR/src/Plugins/"
cp -r "$SCRIPT_DIR/Plugins/Nop.Plugin.Misc.InstagramAssistant" "$NOP_DIR/src/Plugins/"
cp -r "$SCRIPT_DIR/Plugins/Nop.Plugin.Misc.MasterSiteHub" "$NOP_DIR/src/Plugins/"

cd "$NOP_DIR"

echo "==> افزودن پلاگین‌ها به NopCommerce.sln ..."
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.MultiTenantStores/Nop.Plugin.Misc.MultiTenantStores.csproj --in-root 2>/dev/null || true
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.InstagramAssistant/Nop.Plugin.Misc.InstagramAssistant.csproj --in-root 2>/dev/null || true
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.MasterSiteHub/Nop.Plugin.Misc.MasterSiteHub.csproj --in-root 2>/dev/null || true
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Api/Nop.Plugin.Api.csproj --in-root 2>/dev/null || true

echo "==> Restore ..."
dotnet restore src/NopCommerce.sln

echo "==> Build هسته MultiTenantStores ..."
dotnet build src/Plugins/Nop.Plugin.Misc.MultiTenantStores/Nop.Plugin.Misc.MultiTenantStores.csproj -c Release --no-restore -v minimal

echo "==> Build سایر پلاگین‌ها ..."
dotnet build src/Plugins/Nop.Plugin.Api/Nop.Plugin.Api.csproj -c Release --no-restore -v minimal
dotnet build src/Plugins/Nop.Plugin.Misc.MasterSiteHub/Nop.Plugin.Misc.MasterSiteHub.csproj -c Release --no-restore -v minimal
dotnet build src/Plugins/Nop.Plugin.Misc.InstagramAssistant/Nop.Plugin.Misc.InstagramAssistant.csproj -c Release --no-restore -v minimal

echo ""
echo "✅ بیلد موفق!"
echo "خروجی‌ها در:"
echo "  $NOP_DIR/src/Presentation/Nop.Web/Plugins/Misc.MultiTenantStores"
echo "  $NOP_DIR/src/Presentation/Nop.Web/Plugins/Api"
echo "  $NOP_DIR/src/Presentation/Nop.Web/Plugins/Misc.MasterSiteHub"
echo "  $NOP_DIR/src/Presentation/Nop.Web/Plugins/Misc.InstagramAssistant"
ls -lh src/Presentation/Nop.Web/Plugins/Misc.MultiTenantStores/*.dll | tail -n 5
