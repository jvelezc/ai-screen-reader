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
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-v", "error", "-threads", "1", "-i", input, "-vsync", "0",
                "-frames:v", (PngFrameReader.MaximumFrames + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                // The pinned minimal build supplies image2, not the image2pipe output muxer.
                // Its pipe protocol appends each complete PNG without creating frame files.
                "-threads", "1", "-f", "image2", "-update", "1", "-c:v", "png", "pipe:1" }) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new BrowserOperationException("CAPTURE_DECODER_UNAVAILABLE");
            var errors = DrainErrorsAsync(process.StandardError.BaseStream);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var frames = await PngFrameReader.ReadAsync(process.StandardOutput.BaseStream, timeout.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await errors.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new BrowserOperationException("CAPTURE_DECODE_FAILED");
                if (frames.IsEmpty) throw new BrowserOperationException("CAPTURE_FRAME_COUNT_INVALID");
                return frames;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new BrowserOperationException("CAPTURE_DECODE_TIMEOUT");
            }
            finally
            {
                if (!process.HasExited)
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync().ConfigureAwait(false);
                await errors.ConfigureAwait(false);
            }
        }
        finally
        {
            // Only known leaf files in this freshly owned directory; no recursive deletion.
            if (File.Exists(input)) File.Delete(input);
            Directory.Delete(root);
        }
    }

    private static async Task DrainErrorsAsync(Stream errors)
    {
        var buffer = new byte[4096];
        while (await errors.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
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
