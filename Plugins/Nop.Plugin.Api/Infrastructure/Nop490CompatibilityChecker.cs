namespace Nop.Plugin.Api.Infrastructure
{
    using System;
    using System.Linq;
    using System.Reflection;
    using Nop.Services.Orders;
    using Nop.Services.Catalog;
    using Nop.Services.Stores;

    /// <summary>
    /// بررسی سازگاری نسخهٔ nopCommerce نصب‌شده با امضای متدهایی که این پلاگین‌ها به آن وابسته‌اند —
    /// در صورت به‌روزرسانی nopCommerce، اگر امضای این متدها تغییر کند، این کلاس در زمان راه‌اندازی
    /// (Startup) هشدار می‌دهد به‌جای بروز خطای مبهم در زمان اجرا.
    /// </summary>
    public static class Nop490CompatibilityChecker
    {
        public static CompatibilityReport VerifyCoreSignatures()
        {
            var report = new CompatibilityReport
            {
                FrameworkVersion = Environment.Version.ToString(),
                TargetNopVersion = "4.90",
                VerifiedAtUtc = DateTime.UtcNow
            };

            report.OrderServiceValid = MethodExists(typeof(IOrderService), "SearchOrdersAsync");
            report.StoreMappingValid = MethodExists(typeof(IStoreMappingService), "AuthorizeAsync");
            report.CategoryServiceValid = MethodExists(typeof(ICategoryService), "GetAllCategoriesAsync");

            report.IsAllValid = report.OrderServiceValid && report.StoreMappingValid && report.CategoryServiceValid;

            return report;
        }

        private static bool MethodExists(Type type, string methodName)
        {
            try
            {
                return type.GetMethods().Any(m => m.Name == methodName);
            }
            catch (AmbiguousMatchException)
            {
                // چند Overload با همین نام یافت شد — یعنی متد وجود دارد، فقط باید مشخص‌تر جستجو شود
                return true;
            }
        }
    }

    public class CompatibilityReport
    {
        public string FrameworkVersion { get; set; }
        public string TargetNopVersion { get; set; }
        public DateTime VerifiedAtUtc { get; set; }
        public bool OrderServiceValid { get; set; }
        public bool StoreMappingValid { get; set; }
        public bool CategoryServiceValid { get; set; }
        public bool IsAllValid { get; set; }
    }
}