using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace WedNightFury.Services
{
    public class GeoCodingService
    {
        private readonly IHttpClientFactory _factory;

        public GeoCodingService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<(double? lat, double? lng, string? err)> GeocodeAsync(string address, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(address))
                return (null, null, "Empty address");

            // thử 2 biến thể: có dấu + không dấu (tăng tỉ lệ match)
            var a1 = address.Trim();
            var a2 = RemoveDiacritics(a1);

            var candidates = new[] { a1, a2 }.Distinct().ToArray();

            foreach (var q in candidates)
            {
                var (lat, lng, err) = await GeocodeOnceAsync(q, ct);
                if (lat.HasValue && lng.HasValue) return (lat, lng, null);
            }

            return (null, null, "No result");
        }

        private async Task<(double? lat, double? lng, string? err)> GeocodeOnceAsync(string address, CancellationToken ct)
        {
            var http = _factory.CreateClient("nominatim");

            // countrycodes=vn để ưu tiên VN
            var url =
                $"search?format=jsonv2&limit=1&countrycodes=vn&q={Uri.EscapeDataString(address)}";

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using var res = await http.GetAsync(url, ct);

                if (res.StatusCode == (HttpStatusCode)429)
                {
                    await Task.Delay(800 * attempt, ct); // backoff
                    continue;
                }

                if (!res.IsSuccessStatusCode)
                    return (null, null, $"HTTP {(int)res.StatusCode}");

                var json = await res.Content.ReadAsStringAsync(ct);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                    return (null, null, "No result");

                var first = root[0];

                var latStr = first.GetProperty("lat").GetString();
                var lonStr = first.GetProperty("lon").GetString();

                if (!double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat))
                    return (null, null, "Parse lat fail");

                if (!double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
                    return (null, null, "Parse lng fail");

                return (lat, lng, null);
            }

            return (null, null, "429 Too Many Requests");
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var ch in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return sb.ToString()
                .Normalize(NormalizationForm.FormC);
        }
    }
}
