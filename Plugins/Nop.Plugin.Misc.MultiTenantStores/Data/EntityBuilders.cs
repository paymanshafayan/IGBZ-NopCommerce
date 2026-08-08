namespace Nop.Plugin.Misc.MultiTenantStores.Data
{
    using FluentMigrator.Builders.Create.Table;
    using Nop.Data.Mapping.Builders;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    public class StoreDomainMappingBuilder : NopEntityBuilder<StoreDomainMapping>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(StoreDomainMapping.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(StoreDomainMapping.HostName)).AsString(400).NotNullable()
                .WithColumn(nameof(StoreDomainMapping.IsPrimaryDomain)).AsBoolean().NotNullable()
                .WithColumn(nameof(StoreDomainMapping.IsActive)).AsBoolean().NotNullable()
                .WithColumn(nameof(StoreDomainMapping.IsSslVerified)).AsBoolean().NotNullable()
                .WithColumn(nameof(StoreDomainMapping.CreatedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(StoreDomainMapping.UpdatedOnUtc)).AsDateTime2().NotNullable();
        }
    }

    public class TenantPlanBuilder : NopEntityBuilder<TenantPlan>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(TenantPlan.Name)).AsString(200).NotNullable()
                .WithColumn(nameof(TenantPlan.SystemName)).AsString(200).NotNullable()
                .WithColumn(nameof(TenantPlan.Description)).AsString(int.MaxValue).Nullable()
                .WithColumn(nameof(TenantPlan.LinkedProductId)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantPlan.MaxProductsAllowed)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantPlan.MaxOrdersPerMonth)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantPlan.AllowCustomDomain)).AsBoolean().NotNullable()
                .WithColumn(nameof(TenantPlan.AllowDedicatedApp)).AsBoolean().NotNullable()
                .WithColumn(nameof(TenantPlan.AllowStore)).AsBoolean().NotNullable()
                .WithColumn(nameof(TenantPlan.AllowInstagramAiAssistant)).AsBoolean().NotNullable()
                .WithColumn(nameof(TenantPlan.AllowInstagramAiAssistantPro)).AsBoolean().NotNullable()
                .WithColumn(nameof(TenantPlan.PriceMonthly)).AsDecimal(18, 4).NotNullable()
                .WithColumn(nameof(TenantPlan.PriceSixMonths)).AsDecimal(18, 4).NotNullable()
                .WithColumn(nameof(TenantPlan.PriceYearly)).AsDecimal(18, 4).NotNullable()
                .WithColumn(nameof(TenantPlan.TrialDurationDays)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantPlan.DisplayOrder)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantPlan.IsActive)).AsBoolean().NotNullable();
        }
    }

    public class TenantStoreSubscriptionBuilder : NopEntityBuilder<TenantStoreSubscription>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(TenantStoreSubscription.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantStoreSubscription.TenantPlanId)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantStoreSubscription.OwnerCustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantStoreSubscription.Status)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantStoreSubscription.TrialEndDateUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(TenantStoreSubscription.StartDateUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(TenantStoreSubscription.NextBillingDateUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(TenantStoreSubscription.AutoRenew)).AsBoolean().NotNullable()
                .WithColumn(nameof(TenantStoreSubscription.CreatedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(TenantStoreSubscription.UpdatedOnUtc)).AsDateTime2().NotNullable();
        }
    }

    public class TenantIntegrationCredentialBuilder : NopEntityBuilder<TenantIntegrationCredential>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(TenantIntegrationCredential.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(TenantIntegrationCredential.ProviderKey)).AsString(100).NotNullable()
                .WithColumn(nameof(TenantIntegrationCredential.ApiKey)).AsString(2000).Nullable()
                .WithColumn(nameof(TenantIntegrationCredential.ApiSecret)).AsString(2000).Nullable()
                .WithColumn(nameof(TenantIntegrationCredential.EndpointOverrideUrl)).AsString(500).Nullable()
                .WithColumn(nameof(TenantIntegrationCredential.IsActive)).AsBoolean().NotNullable()
                .WithColumn(nameof(TenantIntegrationCredential.IsVerified)).AsBoolean().NotNullable()
                .WithColumn(nameof(TenantIntegrationCredential.CreatedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(TenantIntegrationCredential.UpdatedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(TenantIntegrationCredential.LastTestedOnUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(TenantIntegrationCredential.LastTestResultMessage)).AsString(1000).Nullable();
        }
    }

    public class PaymentTransactionLedgerBuilder : NopEntityBuilder<PaymentTransactionLedger>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(PaymentTransactionLedger.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(PaymentTransactionLedger.OrderId)).AsInt32().NotNullable()
                .WithColumn(nameof(PaymentTransactionLedger.GatewayName)).AsString(100).NotNullable()
                .WithColumn(nameof(PaymentTransactionLedger.TrackingNumber)).AsString(100).NotNullable()
                .WithColumn(nameof(PaymentTransactionLedger.AmountToman)).AsDecimal(18, 4).NotNullable()
                .WithColumn(nameof(PaymentTransactionLedger.State)).AsInt32().NotNullable()
                .WithColumn(nameof(PaymentTransactionLedger.BankRefId)).AsString(200).Nullable()
                .WithColumn(nameof(PaymentTransactionLedger.RawGatewayResponse)).AsString(int.MaxValue).Nullable()
                .WithColumn(nameof(PaymentTransactionLedger.RequestedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(PaymentTransactionLedger.VerifiedOnUtc)).AsDateTime2().Nullable();
        }
    }

    public class PendingMarketplaceSyncBuilder : NopEntityBuilder<PendingMarketplaceSync>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(PendingMarketplaceSync.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(PendingMarketplaceSync.ProductId)).AsInt32().NotNullable()
                .WithColumn(nameof(PendingMarketplaceSync.ProviderKey)).AsString(100).NotNullable()
                .WithColumn(nameof(PendingMarketplaceSync.Action)).AsInt32().NotNullable()
                .WithColumn(nameof(PendingMarketplaceSync.IsProcessed)).AsBoolean().NotNullable()
                .WithColumn(nameof(PendingMarketplaceSync.LastError)).AsString(2000).Nullable()
                .WithColumn(nameof(PendingMarketplaceSync.AttemptCount)).AsInt32().NotNullable()
                .WithColumn(nameof(PendingMarketplaceSync.CreatedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(PendingMarketplaceSync.ProcessedOnUtc)).AsDateTime2().Nullable();
        }
    }

    public class AffiliateReferralCodeBuilder : NopEntityBuilder<AffiliateReferralCode>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(AffiliateReferralCode.CustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateReferralCode.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateReferralCode.Code)).AsString(20).NotNullable()
                .WithColumn(nameof(AffiliateReferralCode.CreatedOnUtc)).AsDateTime2().NotNullable();
        }
    }

    public class AffiliateCommissionLedgerBuilder : NopEntityBuilder<AffiliateCommissionLedger>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(AffiliateCommissionLedger.ReferrerCustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateCommissionLedger.ReferredCustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateCommissionLedger.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateCommissionLedger.OrderId)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateCommissionLedger.CommissionToman)).AsDecimal(18, 4).NotNullable()
                .WithColumn(nameof(AffiliateCommissionLedger.State)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateCommissionLedger.CreatedOnUtc)).AsDateTime2().NotNullable();
        }
    }

    public class AffiliateWithdrawalRequestBuilder : NopEntityBuilder<AffiliateWithdrawalRequest>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(AffiliateWithdrawalRequest.CustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateWithdrawalRequest.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateWithdrawalRequest.AmountToman)).AsDecimal(18, 4).NotNullable()
                .WithColumn(nameof(AffiliateWithdrawalRequest.Status)).AsInt32().NotNullable()
                .WithColumn(nameof(AffiliateWithdrawalRequest.BankAccountInfo)).AsString(500).Nullable()
                .WithColumn(nameof(AffiliateWithdrawalRequest.AdminNote)).AsString(1000).Nullable()
                .WithColumn(nameof(AffiliateWithdrawalRequest.RequestedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(AffiliateWithdrawalRequest.ProcessedOnUtc)).AsDateTime2().Nullable();
        }
    }

    public class CourseLessonBuilder : NopEntityBuilder<CourseLesson>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(CourseLesson.ProductId)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseLesson.Title)).AsString(400).NotNullable()
                .WithColumn(nameof(CourseLesson.DisplayOrder)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseLesson.DurationMinutes)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseLesson.VodVideoPath)).AsString(1000).Nullable()
                .WithColumn(nameof(CourseLesson.AttachmentUrl)).AsString(1000).Nullable()
                .WithColumn(nameof(CourseLesson.IsFreePreview)).AsBoolean().NotNullable()
                .WithColumn(nameof(CourseLesson.CreatedOnUtc)).AsDateTime2().NotNullable();
        }
    }

    public class CourseQuizQuestionBuilder : NopEntityBuilder<CourseQuizQuestion>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(CourseQuizQuestion.ProductId)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseQuizQuestion.DisplayOrder)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseQuizQuestion.QuestionText)).AsString(2000).NotNullable();
        }
    }

    public class CourseQuizOptionBuilder : NopEntityBuilder<CourseQuizOption>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(CourseQuizOption.QuestionId)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseQuizOption.OptionText)).AsString(1000).NotNullable()
                .WithColumn(nameof(CourseQuizOption.IsCorrect)).AsBoolean().NotNullable();
        }
    }

    public class CourseEnrollmentProgressBuilder : NopEntityBuilder<CourseEnrollmentProgress>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(CourseEnrollmentProgress.CustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseEnrollmentProgress.ProductId)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseEnrollmentProgress.LessonId)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseEnrollmentProgress.IsCompleted)).AsBoolean().NotNullable()
                .WithColumn(nameof(CourseEnrollmentProgress.CompletedOnUtc)).AsDateTime2().Nullable();
        }
    }

    public class CourseCertificateBuilder : NopEntityBuilder<CourseCertificate>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(CourseCertificate.CustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseCertificate.ProductId)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseCertificate.CertificateCode)).AsString(200).NotNullable()
                .WithColumn(nameof(CourseCertificate.QuizScorePercent)).AsInt32().NotNullable()
                .WithColumn(nameof(CourseCertificate.IssuedOnUtc)).AsDateTime2().NotNullable();
        }
    }

    public class WalletLedgerBuilder : NopEntityBuilder<WalletLedger>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(WalletLedger.CustomerId)).AsInt32().NotNullable()
                .WithColumn(nameof(WalletLedger.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(WalletLedger.AmountToman)).AsDecimal(18, 4).NotNullable()
                .WithColumn(nameof(WalletLedger.Reason)).AsInt32().NotNullable()
                .WithColumn(nameof(WalletLedger.ReferenceCode)).AsString(200).Nullable()
                .WithColumn(nameof(WalletLedger.CreatedOnUtc)).AsDateTime2().NotNullable();
        }
    }

    public class LandingContentBlockBuilder : NopEntityBuilder<LandingContentBlock>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(LandingContentBlock.PageKey)).AsString(100).NotNullable()
                .WithColumn(nameof(LandingContentBlock.MenuTitle)).AsString(100).NotNullable()
                .WithColumn(nameof(LandingContentBlock.Title)).AsString(300).NotNullable()
                .WithColumn(nameof(LandingContentBlock.SummaryText)).AsString(int.MaxValue).Nullable()
                .WithColumn(nameof(LandingContentBlock.FeatureBulletsText)).AsString(int.MaxValue).Nullable()
                .WithColumn(nameof(LandingContentBlock.ImageUrl)).AsString(1000).Nullable()
                .WithColumn(nameof(LandingContentBlock.CtaText)).AsString(100).Nullable()
                .WithColumn(nameof(LandingContentBlock.DetailFullContent)).AsString(int.MaxValue).Nullable()
                .WithColumn(nameof(LandingContentBlock.DetailImageUrlsText)).AsString(int.MaxValue).Nullable()
                .WithColumn(nameof(LandingContentBlock.DisplayOrder)).AsInt32().NotNullable()
                .WithColumn(nameof(LandingContentBlock.IsActive)).AsBoolean().NotNullable();
        }
    }

    /// <summary>
    /// ذخیرهٔ DB-محور کدهای OTP ورود با شماره موبایل — به‌جای IMemoryCache که با ری‌استارت اپ یا
    /// چند نمونه (Multi-Instance) کدهای در انتظار تایید را از دست می‌داد.
    /// </summary>
    public class PhoneOtpCodeBuilder : NopEntityBuilder<PhoneOtpCode>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(PhoneOtpCode.StoreId)).AsInt32().NotNullable()
                .WithColumn(nameof(PhoneOtpCode.PhoneNumber)).AsString(20).NotNullable()
                .WithColumn(nameof(PhoneOtpCode.CodeHash)).AsString(200).NotNullable()
                .WithColumn(nameof(PhoneOtpCode.CreatedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(PhoneOtpCode.ExpiresOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(PhoneOtpCode.Used)).AsBoolean().NotNullable();
        }
    }
}
