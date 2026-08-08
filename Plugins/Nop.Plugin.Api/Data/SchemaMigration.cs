namespace Nop.Plugin.Api.Data
{
    using FluentMigrator;
    using FluentMigrator.Builders.Create.Table;
    using Nop.Data.Extensions;
    using Nop.Data.Mapping.Builders;
    using Nop.Data.Migrations;
    using Nop.Plugin.Api.Services;

    public class AdminDeviceTokenBuilder : NopEntityBuilder<AdminDeviceToken>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(AdminDeviceToken.AdminCustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(AdminDeviceToken.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(AdminDeviceToken.FcmToken)).AsString(500).NotNullable()
                .WithColumn(nameof(AdminDeviceToken.DeviceName)).AsString(200).Nullable()
                .WithColumn(nameof(AdminDeviceToken.CreatedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(AdminDeviceToken.LastSeenOnUtc)).AsDateTime2().NotNullable();
        }
    }

    [NopMigration("2025/01/01 09:10:00", "Nop.Plugin.Api base schema", MigrationProcessType.Installation)]
    public class SchemaMigration : Migration
    {
        public override void Up()
        {
            if (!Schema.Table(nameof(AdminDeviceToken)).Exists())
                Create.TableFor<AdminDeviceToken>();

            if (Schema.Table(nameof(AdminDeviceToken)).Exists()
                && !Schema.Table(nameof(AdminDeviceToken)).Index("IX_AdminDeviceToken_Store").Exists())
            {
                Create.Index("IX_AdminDeviceToken_Store")
                    .OnTable(nameof(AdminDeviceToken))
                    .OnColumn(nameof(AdminDeviceToken.StoreId)).Ascending();
            }
        }

        public override void Down()
        {
        }
    }
}
