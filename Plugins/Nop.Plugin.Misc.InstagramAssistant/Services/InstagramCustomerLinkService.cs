namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Customers;
    using Nop.Core.Domain.Common;
    using Nop.Data;
    using Nop.Services.Customers;

    public interface IInstagramCustomerLinkService
    {
        Task<Customer> GetCustomerByInstagramScopedIdAsync(string instagramScopedId);
        Task LinkCustomerToInstagramScopedIdAsync(int customerId, string instagramScopedId);
    }

    /// <summary>
    /// ارتباط بین شناسهٔ Instagram-Scoped-ID (IGSID) و حساب مشتری واقعی nopCommerce.
    /// چون nopCommerce به‌طور بومی مفهوم IGSID را نمی‌شناسد، این مقدار به‌صورت یک Generic Attribute
    /// روی Customer ذخیره و برای جست‌وجو از جدول GenericAttribute واقعی خوانده می‌شود
    /// (KeyGroup = "Customer"، دقیقاً همان قراردادی که IGenericAttributeService خودش استفاده می‌کند).
    /// </summary>
    public class InstagramCustomerLinkService : IInstagramCustomerLinkService
    {
        private const string AttributeKey = "InstagramScopedId";

        private readonly IRepository<GenericAttribute> _genericAttributeRepository;
        private readonly ICustomerService _customerService;
        private readonly Nop.Services.Common.IGenericAttributeService _genericAttributeService;

        public InstagramCustomerLinkService(
            IRepository<GenericAttribute> genericAttributeRepository,
            ICustomerService customerService,
            Nop.Services.Common.IGenericAttributeService genericAttributeService)
        {
            _genericAttributeRepository = genericAttributeRepository;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
        }

        public async Task<Customer> GetCustomerByInstagramScopedIdAsync(string instagramScopedId)
        {
            if (string.IsNullOrWhiteSpace(instagramScopedId))
                return null;

            var match = (await _genericAttributeRepository.GetAllAsync(query =>
                query.Where(a => a.KeyGroup == nameof(Customer)
                    && a.Key == AttributeKey
                    && a.Value == instagramScopedId)))
                .FirstOrDefault();

            if (match == null)
                return null;

            return await _customerService.GetCustomerByIdAsync(match.EntityId);
        }

        public async Task LinkCustomerToInstagramScopedIdAsync(int customerId, string instagramScopedId)
        {
            var customer = await _customerService.GetCustomerByIdAsync(customerId);
            if (customer == null)
                return;

            await _genericAttributeService.SaveAttributeAsync(customer, AttributeKey, instagramScopedId);
        }
    }
}
