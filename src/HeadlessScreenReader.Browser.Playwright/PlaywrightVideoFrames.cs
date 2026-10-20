using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>Uses the decoder installed with the pinned Playwright package, never a system fallback.</summary>
internal static class PlaywrightVideoFrames
{
    public static async Task<ImmutableArray<byte[]>> DecodeAsync(byte[] video)
    {
        if (video.Length is 0 or > 16 * 1024 * 1024) throw new BrowserOperationException("CAPTURE_MEDIA_SIZE_INVALID");
        var executable = ResolveExecutable();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hsr-video-decode-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(root)) throw new BrowserOperationException("CAPTURE_DECODE_DIRECTORY_EXISTS");
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "source.webm");
        try
        {
            await using (var stream = new FileStream(input, FileMode.CreateNew))
                await stream.WriteAsync(video).ConfigureAwait(false);
            var start = new ProcessStartInfo(executable)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-v", "error", "-threads", "1", "-i", input, "-vsync", "0",
                "-frames:v", "251", "-threads", "1", Path.Combine(root, "frame-%04d.png") }) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new BrowserOperationException("CAPTURE_DECODER_UNAVAILABLE");
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                _ = await errors.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new BrowserOperationException("CAPTURE_DECODE_FAILED");
            }
            catch (TimeoutException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
                throw new BrowserOperationException("CAPTURE_DECODE_TIMEOUT");
            }
            var files = Directory.GetFiles(root, "frame-*.png").Order(StringComparer.Ordinal).ToArray();
            if (files.Length is 0 or > 250) throw new BrowserOperationException("CAPTURE_FRAME_COUNT_INVALID");
            var frames = ImmutableArray.CreateBuilder<byte[]>();
            foreach (var file in files)
            {
                var bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                if (bytes.Length is 0 or > 4 * 1024 * 1024) throw new BrowserOperationException("CAPTURE_FRAME_SIZE_INVALID");
                frames.Add(bytes);
            }
            return frames.ToImmutable();
        }
        finally
        {
            // Only known leaf files in this freshly owned directory; no recursive deletion.
            if (File.Exists(input)) File.Delete(input);
            foreach (var file in Directory.GetFiles(root, "frame-*.png")) File.Delete(file);
            Directory.Delete(root);
        }
    }

    private static string ResolveExecutable()
    {
        var package = Path.Combine(Path.GetDirectoryName(typeof(Microsoft.Playwright.Playwright).Assembly.Location)!, ".playwright", "package");
        var manifestPath = Path.Combine(package, "browsers.json");
        if (!File.Exists(manifestPath)) throw new BrowserOperationException("PLAYWRIGHT_DECODER_MANIFEST_MISSING");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var descriptor = manifest.RootElement.GetProperty("browsers").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "ffmpeg");
        var revision = descriptor.GetProperty("revision").GetString()!;
        if (!revision.All(char.IsAsciiDigit) || descriptor.TryGetProperty("revisionOverrides", out _))
            throw new BrowserOperationException("PLAYWRIGHT_DECODER_REVISION_UNSUPPORTED");
        var configured = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cache = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Caches")
            : Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(home, ".cache");
        var browsers = configured == "0" ? Path.Combine(package, ".local-browsers")
            : string.IsNullOrEmpty(configured) ? Path.Combine(cache, "ms-playwright") : configured;
        if (!Path.IsPathRooted(browsers)) browsers = Path.GetFullPath(browsers,
            Environment.GetEnvironmentVariable("INIT_CWD") ?? Directory.GetCurrentDirectory());
        var executable = Path.Combine(browsers, "ffmpeg-" + revision,
            OperatingSystem.IsWindows() ? "ffmpeg-win64.exe" : OperatingSystem.IsMacOS() ? "ffmpeg-mac" : "ffmpeg-linux");
        if (!File.Exists(executable)) throw new BrowserOperationException("PLAYWRIGHT_FFMPEG_NOT_INSTALLED");
        return executable;
    }
}
