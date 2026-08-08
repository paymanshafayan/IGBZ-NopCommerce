namespace Nop.Plugin.Misc.InstagramAssistant.Data
{
    using FluentMigrator;
    using FluentMigrator.Builders.Create.Table;
    using Nop.Data.Extensions;
    using Nop.Data.Mapping.Builders;
    using Nop.Data.Migrations;
    using Nop.Plugin.Misc.InstagramAssistant.Domain;

    public class InstagramFollowMentionRewardBuilder : NopEntityBuilder<InstagramFollowMentionReward>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(InstagramFollowMentionReward.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(InstagramFollowMentionReward.InstagramScopedId)).AsString(100).NotNullable()
                .WithColumn(nameof(InstagramFollowMentionReward.CustomerId)).AsInt32().Nullable()
                .WithColumn(nameof(InstagramFollowMentionReward.CouponCode)).AsString(50).NotNullable()
                .WithColumn(nameof(InstagramFollowMentionReward.DirectMessageSent)).AsBoolean().NotNullable()
                .WithColumn(nameof(InstagramFollowMentionReward.FallbackCommentPosted)).AsBoolean().NotNullable()
                .WithColumn(nameof(InstagramFollowMentionReward.IssuedOnUtc)).AsDateTime2().NotNullable();
        }
    }

    /// <summary>
    /// یادداشت: CustomerWalletLedgerBuilder و جدول مربوطه از این‌جا حذف شدند — کیف‌پول به یک
    /// دفترکل واحد در هستهٔ پلتفرم (WalletLedger در MultiTenantStores) منتقل شد تا اعتبار AI،
    /// کش‌بک، حمایت مالی، کمیسیون Affiliate و پرداخت سفارش همه از یک منبع خوانده/نوشته شوند.
    /// </summary>
    [NopMigration("2025/01/01 09:05:00", "Nop.Plugin.Misc.InstagramAssistant base schema", MigrationProcessType.Installation)]
    public class SchemaMigration : Migration
    {
        public override void Up()
        {
            if (!Schema.Table(nameof(InstagramFollowMentionReward)).Exists())
                Create.TableFor<InstagramFollowMentionReward>();

            // قاعدهٔ الزامی: هر کاربر اینستاگرام فقط یک‌بار در هر فروشگاه می‌تواند از فالو+منشن
            // پاداش بگیرد — این Unique Index تضمین سطح دیتابیس همین قاعده است (نه فقط منطق برنامه،
            // که در برابر دو Request هم‌زمان از همان کاربر آسیب‌پذیر است).
            if (Schema.Table(nameof(InstagramFollowMentionReward)).Exists()
                && !Schema.Table(nameof(InstagramFollowMentionReward)).Index("IX_InstagramFollowMentionReward_Store_User").Exists())
            {
                Create.Index("IX_InstagramFollowMentionReward_Store_User")
                    .OnTable(nameof(InstagramFollowMentionReward))
                    .OnColumn(nameof(InstagramFollowMentionReward.StoreId)).Ascending()
                    .OnColumn(nameof(InstagramFollowMentionReward.InstagramScopedId)).Ascending()
                    .WithOptions().Unique();
            }
        }

        public override void Down()
        {
        }
    }
}
