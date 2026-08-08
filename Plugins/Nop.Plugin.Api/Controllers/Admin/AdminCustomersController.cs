namespace Nop.Plugin.Api.Controllers.Admin
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Services.Customers;
    using Nop.Services.Orders;

    [ApiController]
    [Route("api/admin/customers")]
    public class AdminCustomersController : AuthorizedTenantOwnerApiController
    {
        private readonly ICustomerService _customerService;
        private readonly IOrderService _orderService;

        public AdminCustomersController(
            IWorkContext workContext,
            IStoreContext storeContext,
            ICustomerService customerService,
            IOrderService orderService) : base(workContext, storeContext)
        {
            _customerService = customerService;
            _orderService = orderService;
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchCustomers(
            [FromQuery] string query = null,
            [FromQuery] decimal? minSpent = null,
            [FromQuery] int? minOrders = null,
            [FromQuery] DateTime? createdFromUtc = null,
            [FromQuery] DateTime? createdToUtc = null,
            [FromQuery] int pageIndex = 0,
            [FromQuery] int pageSize = 15)
        {
            var store = await GetAuthorizedStoreAsync();

            var orders = await _orderService.SearchOrdersAsync(
                storeId: store.Id,
                createdFromUtc: createdFromUtc,
                createdToUtc: createdToUtc
            );

            var customerStats = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    OrdersCount = g.Count(),
                    TotalSpent = g.Sum(o => o.OrderTotal),
                    LastOrderDate = g.Max(o => o.CreatedOnUtc)
                })
                .Where(x => (!minSpent.HasValue || x.TotalSpent >= minSpent.Value) &&
                            (!minOrders.HasValue || x.OrdersCount >= minOrders.Value))
                .ToDictionary(x => x.CustomerId, x => x);

            var pagedCustomers = await _customerService.GetAllCustomersAsync(
                storeId: store.Id,
                pageIndex: pageIndex,
                pageSize: pageSize
            );

            var resultItems = pagedCustomers
                .Where(c => string.IsNullOrEmpty(query) ||
                            (c.Email != null && c.Email.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                            (c.Phone != null && c.Phone.Contains(query)))
                .Select(c =>
                {
                    var hasStats = customerStats.TryGetValue(c.Id, out var stats);
                    return new CustomerAdminListDto
                    {
                        Id = c.Id,
                        Email = c.Email,
                        // فیلد واقعی Customer در nopCommerce 4.90 «Phone» است (نه PhoneNumber — طبق
                        // ممیزی مقابل سورس واقعی؛ PhoneNumber در این نسخه وجود ندارد و خطای Build می‌داد).
                        PhoneNumber = c.Phone,
                        RegisteredInStoreId = c.RegisteredInStoreId,
                        CreatedOnUtc = c.CreatedOnUtc,
                        OrdersCount = hasStats ? stats.OrdersCount : 0,
                        TotalSpent = hasStats ? stats.TotalSpent : 0,
                        LastOrderDate = hasStats ? stats.LastOrderDate : (DateTime?)null
                    };
                })
                .ToList();

            return Ok(new
            {
                TotalCount = pagedCustomers.TotalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                TotalPages = pagedCustomers.TotalPages,
                Items = resultItems
            });
        }
    }

    public class CustomerAdminListDto
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public int RegisteredInStoreId { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public int OrdersCount { get; set; }
        public decimal TotalSpent { get; set; }
        public DateTime? LastOrderDate { get; set; }
    }
}