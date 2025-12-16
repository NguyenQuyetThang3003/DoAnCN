using System.Globalization;
using System.Net;
using System.Text.Json;

namespace WedNightFury.Services
{
    public interface IGeoCodingService
    {
        Task<(double? lat, double? lng, string? err)> GeocodeAsync(string address, CancellationToken ct);
    }

    public class NominatimGeoCodingService : IGeoCodingService
    {
        private readonly IHttpClientFactory _factory;

        // ✅ throttle 1 req/s toàn app
        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static DateTime _lastCallUtc = DateTime.MinValue;

        public NominatimGeoCodingService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<(double? lat, double? lng, string? err)> GeocodeAsync(string address, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(address))
                return (null, null, "Empty address");

            // Nominatim dễ “no result” nếu thiếu quốc gia
            var q = $"{address}, Việt Nam";

            // ✅ throttle
            await _gate.WaitAsync(ct);
            try
            {
                var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastCallUtc);
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);

                _lastCallUtc = DateTime.UtcNow;
            }
            finally
            {
                _gate.Release();
            }

            var http = _factory.CreateClient("nominatim");

            // jsonv2 hoặc json đều được; jsonv2 dễ đọc
            var url = $"search?format=jsonv2&limit=1&countrycodes=vn&q={Uri.EscapeDataString(q)}";

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linked.CancelAfter(TimeSpan.FromSeconds(10));

                    using var res = await http.GetAsync(url, linked.Token);

                    if (res.StatusCode == (HttpStatusCode)429)
                    {
                        // retry-after nếu có
                        var delay = 1000 * attempt;
                        if (res.Headers.TryGetValues("Retry-After", out var vals))
                        {
                            var v = vals.FirstOrDefault();
                            if (int.TryParse(v, out var sec) && sec > 0) delay = sec * 1000;
                        }

                        await Task.Delay(delay, ct);
                        continue;
                    }

                    var body = await res.Content.ReadAsStringAsync(linked.Token);

                    if (res.StatusCode == HttpStatusCode.Forbidden)
                        return (null, null, "403 Forbidden (Nominatim chặn request - kiểm tra User-Agent)");

                    if (!res.IsSuccessStatusCode)
                        return (null, null, $"HTTP {(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}");

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                        return (null, null, "No result");

                    var first = root[0];

                    var latStr = first.GetProperty("lat").GetString();
                    var lonStr = first.GetProperty("lon").GetString();

                    if (!double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat))
                        return (null, null, "Parse lat fail");
                    if (!double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
                        return (null, null, "Parse lon fail");

                    return (lat, lng, null);
                }
                catch (TaskCanceledException)
                {
                    if (attempt < 3) continue;
                    return (null, null, "Timeout");
                }
                catch (Exception ex)
                {
                    if (attempt < 3)
                    {
                        await Task.Delay(300 * attempt, ct);
                        continue;
                    }
                    return (null, null, ex.Message);
                }
            }

            return (null, null, "Geocode failed after retries");
        }
    }
}
