namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading.Tasks;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    public interface IStoreDomainMappingService
    {
        Task<StoreDomainMapping> GetByHostNameAsync(string hostName);

        /// <summary>
        /// بررسی اینکه آیا hostName (فعال یا غیرفعال) قبلاً به فروشگاهی اختصاص داده شده — برخلاف
        /// <see cref="GetByHostNameAsync"/> که فقط نگاشت‌های فعال را می‌بیند؛ این برای جلوگیری از
        /// Provision مجدد یک زیردامنهٔ در انتظار پرداخت لازم است.
        /// </summary>
        Task<bool> HostNameExistsAsync(string hostName);

        Task<IList<StoreDomainMapping>> GetMappingsByStoreIdAsync(int storeId);
        Task<IList<StoreDomainMapping>> GetAllMappingsAsync();
        Task InsertMappingAsync(StoreDomainMapping mapping);
        Task UpdateMappingAsync(StoreDomainMapping mapping);
        Task DeleteMappingAsync(StoreDomainMapping mapping);
        Task SetPrimaryDomainAsync(int storeId, int mappingId);
        Task<bool> VerifyCustomDomainCnameAsync(string domainName, string expectedTargetCname);
    }

    public class StoreDomainMappingService : IStoreDomainMappingService
    {
        private readonly IRepository<StoreDomainMapping> _mappingRepository;

        public StoreDomainMappingService(IRepository<StoreDomainMapping> mappingRepository)
        {
            _mappingRepository = mappingRepository;
        }

        public async Task<StoreDomainMapping> GetByHostNameAsync(string hostName)
        {
            if (string.IsNullOrWhiteSpace(hostName))
                return null;

            var cleanHost = hostName.Trim().ToLowerInvariant();
            
            // حذف پورت در صورت وجود (مانند store1.market.com:443)
            if (cleanHost.Contains(":"))
                cleanHost = cleanHost.Split(':')[0];

            return await _mappingRepository.Table
                .Where(m => m.IsActive && m.HostName.ToLower() == cleanHost)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> HostNameExistsAsync(string hostName)
        {
            if (string.IsNullOrWhiteSpace(hostName))
                return false;

            var cleanHost = hostName.Trim().ToLowerInvariant();
            if (cleanHost.Contains(":"))
                cleanHost = cleanHost.Split(':')[0];

            var matches = await _mappingRepository.GetAllAsync(q =>
                q.Where(m => m.HostName.ToLower() == cleanHost));
            return matches.Any();
        }

        public async Task<IList<StoreDomainMapping>> GetMappingsByStoreIdAsync(int storeId)
        {
            return await _mappingRepository.GetAllAsync(query =>
                query.Where(m => m.StoreId == storeId)
                     .OrderByDescending(m => m.IsPrimaryDomain)
                     .ThenBy(m => m.CreatedOnUtc));
        }

        public async Task<IList<StoreDomainMapping>> GetAllMappingsAsync()
        {
            return await _mappingRepository.GetAllAsync(query =>
                query.OrderByDescending(m => m.CreatedOnUtc));
        }

        public async Task InsertMappingAsync(StoreDomainMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            mapping.HostName = mapping.HostName.Trim().ToLowerInvariant();
            mapping.CreatedOnUtc = DateTime.UtcNow;
            mapping.UpdatedOnUtc = DateTime.UtcNow;

            await _mappingRepository.InsertAsync(mapping);
        }

        public async Task UpdateMappingAsync(StoreDomainMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            mapping.UpdatedOnUtc = DateTime.UtcNow;
            await _mappingRepository.UpdateAsync(mapping);
        }

        public async Task DeleteMappingAsync(StoreDomainMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            await _mappingRepository.DeleteAsync(mapping);
        }

        public async Task SetPrimaryDomainAsync(int storeId, int mappingId)
        {
            var mappings = await GetMappingsByStoreIdAsync(storeId);
            foreach (var m in mappings)
            {
                m.IsPrimaryDomain = (m.Id == mappingId);
                m.UpdatedOnUtc = DateTime.UtcNow;
                await _mappingRepository.UpdateAsync(m);
            }
        }

        /// <summary>
        /// بررسی واقعی رکورد CNAME دامنه از طریق یک کوئری DNS (QTYPE=CNAME) به سرور DNS سیستم —
        /// نسخهٔ قبلی فقط چک می‌کرد دامنه اصلاً resolve می‌شود (هر دامنهٔ زنده‌ای، حتی با CNAME
        /// اشتباه، «تایید» می‌شد). حالا مقدار CNAME باید دقیقاً برابر target مورد انتظار باشد.
        /// </summary>
        public async Task<bool> VerifyCustomDomainCnameAsync(string domainName, string expectedTargetCname)
        {
            if (string.IsNullOrWhiteSpace(domainName) || string.IsNullOrWhiteSpace(expectedTargetCname))
                return false;

            var normalizedDomain = domainName.Trim().TrimEnd('.').ToLowerInvariant();
            var normalizedTarget = expectedTargetCname.Trim().TrimEnd('.').ToLowerInvariant();

            var actualCname = await ResolveCnameAsync(normalizedDomain);
            if (string.IsNullOrEmpty(actualCname))
                return false;

            return string.Equals(actualCname.TrimEnd('.').ToLowerInvariant(), normalizedTarget, StringComparison.Ordinal);
        }

        // ────────────────────────── کوئری DNS دستی (CNAME) ──────────────────────────
        // دات‌نت API آماده‌ای برای خواندن رکورد CNAME در معرض نمی‌گذارد (Dns.GetHostEntry فقط
        // آدرس IP برمی‌گرداند و aliases قابل‌اتکا نیست)؛ پس یک کوئری UDP سادهٔ DNS (QTYPE=5)
        // به سرور DNS سیستم ارسال و پاسخش پارس می‌شود.

        private static async Task<string> ResolveCnameAsync(string domain)
        {
            var queryId = (ushort)System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 65536);
            var dnsServers = GetDnsServers();

            foreach (var server in dnsServers)
            {
                try
                {
                    using var client = new UdpClient();
                    var endpoint = new IPEndPoint(IPAddress.Parse(server), 53);

                    var queryBytes = BuildCnameQuery(queryId, domain);
                    await client.SendAsync(queryBytes, queryBytes.Length, endpoint);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    var response = await client.ReceiveAsync(cts.Token);

                    var target = ParseCnameResponse(response.Buffer, queryId, domain);
                    if (target != null)
                        return target;
                }
                catch
                {
                    // این سرور DNS در دسترس نبود — سرور بعدی را امتحان کن
                }
            }

            return null;
        }

        private static List<string> GetDnsServers()
        {
            var servers = new List<string>();
            try
            {
                if (File.Exists("/etc/resolv.conf"))
                {
                    foreach (var line in File.ReadAllLines("/etc/resolv.conf"))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("nameserver", StringComparison.Ordinal))
                        {
                            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && IPAddress.TryParse(parts[1].Trim(), out _))
                                servers.Add(parts[1].Trim());
                        }
                    }
                }
            }
            catch
            {
                // بی‌صدا رد شو و از fallback استفاده کن
            }

            if (servers.Count == 0)
            {
                servers.Add("8.8.8.8");
                servers.Add("1.1.1.1");
            }

            return servers;
        }

        private static byte[] BuildCnameQuery(ushort id, string domain)
        {
            using var ms = new MemoryStream();
            WriteUInt16(ms, id);
            WriteUInt16(ms, 0x0100); // Flags: RD=1
            WriteUInt16(ms, 1);      // QDCOUNT
            WriteUInt16(ms, 0);      // ANCOUNT
            WriteUInt16(ms, 0);      // NSCOUNT
            WriteUInt16(ms, 0);      // ARCOUNT

            foreach (var label in domain.Split('.'))
            {
                var labelBytes = Encoding.ASCII.GetBytes(label);
                ms.WriteByte((byte)labelBytes.Length);
                ms.Write(labelBytes, 0, labelBytes.Length);
            }
            ms.WriteByte(0); // انتهای نام

            WriteUInt16(ms, 5); // QTYPE = CNAME
            WriteUInt16(ms, 1); // QCLASS = IN
            return ms.ToArray();
        }

        private static string ParseCnameResponse(byte[] data, ushort expectedId, string questionDomain)
        {
            if (data == null || data.Length < 12)
                return null;

            var id = (ushort)((data[0] << 8) | data[1]);
            if (id != expectedId)
                return null;

            var flags = (ushort)((data[2] << 8) | data[3]);
            if ((flags & 0x8000) == 0) return null;   // QR باید ۱ باشد
            if ((flags & 0x000F) != 0) return null;   // RCODE != 0 (خطای DNS)

            var qdCount = (ushort)((data[4] << 8) | data[5]);
            var anCount = (ushort)((data[6] << 8) | data[7]);

            var offset = 12;
            for (var i = 0; i < qdCount; i++)
            {
                var qName = ReadDnsName(data, ref offset);
                if (qName == null) return null;
                offset += 4; // QTYPE + QCLASS
            }

            for (var i = 0; i < anCount; i++)
            {
                var name = ReadDnsName(data, ref offset);
                if (name == null) return null;
                if (offset + 10 > data.Length) return null;

                var type = (ushort)((data[offset] << 8) | data[offset + 1]);
                var rdataLength = (ushort)((data[offset + 8] << 8) | data[offset + 9]);
                offset += 10;

                if (type == 5 && string.Equals(name, questionDomain, StringComparison.OrdinalIgnoreCase))
                {
                    return ReadDnsName(data, ref offset);
                }

                offset += rdataLength;
            }

            return null;
        }

        /// <summary>خواندن نام DNS با پشتیبانی از اشاره‌گرهای فشرده‌سازی (C0 XX) و محافظت در برابر حلقه.</summary>
        private static string ReadDnsName(byte[] data, ref int offset)
        {
            var sb = new StringBuilder();
            var visited = new HashSet<int>();
            var jumped = false;
            var cursor = offset;

            while (true)
            {
                if (cursor >= data.Length)
                    return null;
                if (!visited.Add(cursor))
                    return null; // حلقهٔ اشاره‌گر — پاسخ خراب

                var lengthByte = data[cursor];

                if (lengthByte == 0)
                {
                    if (!jumped)
                        offset = cursor + 1;
                    break;
                }

                if ((lengthByte & 0xC0) == 0xC0)
                {
                    if (cursor + 1 >= data.Length)
                        return null;
                    var pointer = ((lengthByte & 0x3F) << 8) | data[cursor + 1];
                    if (!jumped)
                        offset = cursor + 2;
                    cursor = pointer;
                    jumped = true;
                    continue;
                }

                if ((lengthByte & 0xC0) != 0)
                    return null; // نوع label ناشناخته

                cursor++;
                if (cursor + lengthByte > data.Length)
                    return null;

                if (sb.Length > 0)
                    sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(data, cursor, lengthByte));
                cursor += lengthByte;
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        private static void WriteUInt16(MemoryStream ms, ushort value)
        {
            ms.WriteByte((byte)(value >> 8));
            ms.WriteByte((byte)(value & 0xFF));
        }
    }
}