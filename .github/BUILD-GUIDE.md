# راهنمای بیلد گرفتن از پروژه IGBZ-NopCommerce

این ریپو فقط ۴ پلاگین دارد، نه خود nopCommerce. برای بیلد، باید سورس رسمی nopCommerce 4.90.6 را به عنوان پایه بیاورید.

## چرا مستقیم `dotnet build` در این ریپو خطا می‌دهد؟

هر `.csproj` این‌طور نوشته شده:
```xml
<OutputPath>$(SolutionDir)\Presentation\Nop.Web\Plugins\...</OutputPath>
<ProjectReference Include="$(SolutionDir)\Presentation\Nop.Web.Framework\Nop.Web.Framework.csproj" />
```
یعنی انتظار دارد داخل یک Solution بزرگ nopCommerce باشد (`src/NopCommerce.sln`). وقتی فقط `Plugins/` را checkout کنید، `$(SolutionDir)` وجود ندارد و `Nop.Web.Framework` پیدا نمی‌شود — پس Restore/Build شکست می‌خورد. این طبیعی است و در `PLACEMENT-GUIDE.md` هم گفته شده.

## روش ۱: بیلد محلی (توسعه‌دهنده) — پیشنهادی

### پیش‌نیاز
- .NET SDK 9.0.100+ : `dotnet --version`
- Git

### قدم‌ها (Linux/macOS)
```bash
# از داخل همین ریپو:
chmod +x build.sh
./build.sh
# اگر قبلاً nopCommerce را دارید:
./build.sh /absolute/path/to/nopCommerce
```

### قدم‌ها (Windows PowerShell)
```powershell
.\build.ps1
# یا
.\build.ps1 -NopDir C:\Dev\nopCommerce-4.90.6
```

اسکریپت این کارها را می‌کند:
1. کلون `https://github.com/nopSolutions/nopCommerce.git` با تگ `release-4.90.6` در `../nopCommerce-4.90.6`
2. کپی ۴ پوشه `Plugins/*` به `nopCommerce/src/Plugins/`
3. `dotnet sln add` هر ۴ پلاگین به `NopCommerce.sln`
4. `dotnet restore` + `dotnet build` به ترتیب وابستگی (ابتدا MultiTenantStores که بقیه به آن وابسته‌اند)

خروجی در:
- `nopCommerce/src/Presentation/Nop.Web/Plugins/Misc.MultiTenantStores/`
- `nopCommerce/src/Presentation/Nop.Web/Plugins/Api/`
- `nopCommerce/src/Presentation/Nop.Web/Plugins/Misc.MasterSiteHub/`
- `nopCommerce/src/Presentation/Nop.Web/Plugins/Misc.InstagramAssistant/`

هر پوشه شامل `.dll` + `plugin.json` + محتویات `Views` است و مستقیماً قابل کپی به سرور واقعی (`/Plugins/...`) است.

### اجرای دستی بدون اسکریپت
```bash
git clone --depth 1 --branch release-4.90.6 https://github.com/nopSolutions/nopCommerce.git
cp -r IGBZ-NopCommerce/Plugins/* nopCommerce/src/Plugins/
cd nopCommerce
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.MultiTenantStores/Nop.Plugin.Misc.MultiTenantStores.csproj
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Api/Nop.Plugin.Api.csproj
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.MasterSiteHub/Nop.Plugin.Misc.MasterSiteHub.csproj
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.InstagramAssistant/Nop.Plugin.Misc.InstagramAssistant.csproj
dotnet restore src/NopCommerce.sln
dotnet build src/Plugins/Nop.Plugin.Misc.MultiTenantStores/Nop.Plugin.Misc.MultiTenantStores.csproj -c Release --no-restore
# سه تای دیگر هم مشابه
```

## روش ۲: بیلد اتوماتیک در Git (GitHub Actions) — CI

فایل `.github/workflows/build.yml` در همین ریپو اضافه شده و روی push به `main` و روی PR اجرا می‌شود.

### چه می‌کند؟
1. Checkout این ریپو (`igbz/`) + Checkout رسمی nopCommerce 4.90.6 (`nopCommerce/`) با `actions/checkout@v4`
2. Setup .NET 9 + Cache NuGet
3. کپی ۴ پلاگین به `nopCommerce/src/Plugins/`
4. `dotnet sln add` + `dotnet restore`
5. Build به ترتیب وابستگی + چک وجود dll
6. Zip هر پلاگین و آپلود به عنوان Artifact (`igbz-plugins-4906`) — از تب Actions می‌توان دانلود کرد

### چطور فعال می‌شود؟
- کافی است همین فایل را به `main` پوش کنید (انجام شده).
- سپس در تب `Actions` گیت‌هاب، Workflow `Build IGBZ Plugins on nopCommerce 4.90.6` را می‌بینید.
- روی هر push جدید، artifact به صورت خودکار تولید می‌شود.

کپی دستی workflow برای ریپوهای دیگر:
```yaml
# .github/workflows/build.yml
# — محتوای فایل فعلی را ببینید —
```

## روش ۳: Docker / CI سفارشی (اختیاری)

اگر Jenkins/GitLab دارید، منطق همان است:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0
WORKDIR /src
RUN git clone --depth 1 --branch release-4.90.6 https://github.com/nopSolutions/nopCommerce.git .
COPY Plugins/* ./src/Plugins/
RUN dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.MultiTenantStores/Nop.Plugin.Misc.MultiTenantStores.csproj && \
    dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.InstagramAssistant/Nop.Plugin.Misc.InstagramAssistant.csproj && \
    dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.MasterSiteHub/Nop.Plugin.Misc.MasterSiteHub.csproj && \
    dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Api/Nop.Plugin.Api.csproj
RUN dotnet restore src/NopCommerce.sln && \
    dotnet build src/NopCommerce.sln -c Release --no-restore
```

## نکات مهم برای جلوگیری از خطاهای رایج

- **نسخه دقیق nopCommerce:** این پلاگین‌ها مقابل سورس واقعی `release-4.90.6` (تگ `e3d129c`) راستی‌آزمایی شده‌اند. از 4.80 یا 4.90.0-beta استفاده نکنید — بعضی APIها (مثل `Customer.Phone` یا `Table` vs `TableAsync`) تغییر کرده.
- **ترتیب Build:** `MultiTenantStores` باید اول ساخته شود، چون ۳ پلاگین دیگر `ProjectReference` به آن دارند.
- **NuGet:** فایل‌های csproj نسخه‌های تقریبی برای `ImageSharp` و `JwtBearer` دارند؛ `dotnet restore` نسخه سازگار با `Directory.Build.props` خود nopCommerce را انتخاب می‌کند. اگر تضاد نسخه دیدید، نسخه را با آنچه در `src/Libraries` یا `src/Presentation` استفاده شده هماهنگ کنید.
- **ClearPluginAssemblies:** هر `.csproj` یک Target به نام `NopTarget` دارد که بعد از Build، dllهای غیرضروری را پاک می‌کند — این به صورت خودکار اجرا می‌شود؛ چیزی دستی لازم نیست.
- **appsettings:** برای تست واقعی، باید کلیدهای تنظیمات الزامی (`MultiTenantStores:VodHmac...` و غیره) را در `nopCommerce/src/Presentation/Nop.Web/appsettings.json` یا User Secrets ست کنید (طبق `PLACEMENT-GUIDE.md` بخش ۳).

## خروجی بیلد چیست؟

یک فولدر برای هر پلاگین، مثلاً:
```
src/Presentation/Nop.Web/Plugins/Misc.MultiTenantStores/
  Nop.Plugin.Misc.MultiTenantStores.dll
  plugin.json
  Views/...
```
همین فولدر را روی سرور nopCommerce در `Plugins/Misc.MultiTenantStores/` کپی کنید و اپ را ری‌استارت کنید (یا از پنل Admin > Plugins > Reload).

