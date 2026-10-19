using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RynthCore.App.Avalonia;

namespace AcDream.Tools.RynthNavData;

/// <summary>
/// Downloads an archive once, checks it with RynthCore's own unpack rules (allowed names,
/// entry size limit, no duplicates) and returns its pin. The verified zip stays in the cache
/// under the name RynthCore's install looks for, so the install that follows reuses it.
/// </summary>
internal static class NavDataPinner
{
    // RynthUpdater.NavData's MaxNavDataBytes (private there): the largest archive accepted.
    private const long MaxArchiveBytes = 3L << 30;

    internal static async Task<NavDataPin> PinAsync(string url, string cacheDir, string rynthSuite, DateOnly today,
        HttpClient http, TextWriter log, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        string part = Path.Combine(cacheDir, "navdata-pin.part");
        long size = 0;
        string sha256;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (Stream body = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                int read;
                long nextReport = 64L << 20;
                while ((read = await body.ReadAsync(buffer, ct)) > 0)
                {
                    size += read;
                    if (size > MaxArchiveBytes)
                        throw new InvalidDataException($"{url} is larger than {MaxArchiveBytes >> 20} MB");
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    if (size >= nextReport)
                    {
                        log.WriteLine($"  {size >> 20} MB");
                        nextReport += 64L << 20;
                    }
                }
            }

            sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        string staging = Path.Combine(cacheDir, "navdata-pin-check");
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            int files = RynthUpdater.ExtractNavData(part, staging, ct);
            long bytes = Directory.GetFiles(staging).Sum(f => new FileInfo(f).Length);
            File.Move(part, Path.Combine(cacheDir, $"navdata-{sha256}.zip"), overwrite: true);
            return new NavDataPin(url, size, sha256, files, bytes, sha256, rynthSuite, today.ToString("yyyy-MM-dd"));
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }
}
