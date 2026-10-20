using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record ProbePixels(int Frames, long CanaryPixels, double DurationSeconds,
    long FirstFrameCanaryPixels, long LastFrameCanaryPixels);

/// <summary>Controlled diagnostic decoder. The pinned recorder emits 25 fps; each frame is inspected.</summary>
internal static class CaptureProbeDecoder
{
    public static async Task<ProbePixels> InspectAsync(IPage page, byte[] bytes, bool video)
    {
        if (bytes.Length is 0 or > 16 * 1024 * 1024) throw new InvalidDataException("CAPTURE_MEDIA_SIZE_INVALID");
        System.Text.Json.JsonElement result;
        try
        {
            result = await page.EvaluateAsync<System.Text.Json.JsonElement>(DecodeScript,
                new { data = Convert.ToBase64String(bytes), video }).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (PlaywrightException exception)
        {
            var code = new[] { "CAPTURE_DECODE_FAILED", "CAPTURE_DURATION_INVALID", "CAPTURE_DIMENSIONS_INVALID",
                "CAPTURE_FRAME_COUNT_INVALID", "CAPTURE_FRAME_NOT_DECODED", "CAPTURE_FRAME_POSITION_MISMATCH", "CAPTURE_DECODE_TIMEOUT" }
                .FirstOrDefault(code => exception.Message.Contains(code, StringComparison.Ordinal));
            throw new HeadlessScreenReader.Domain.BrowserOperationException(code ?? "CAPTURE_DECODE_FAILED");
        }
        return new(result.GetProperty("frames").GetInt32(), result.GetProperty("red").GetInt64(),
            result.GetProperty("duration").GetDouble(), result.GetProperty("firstRed").GetInt64(), result.GetProperty("lastRed").GetInt64());
    }

    private const string DecodeScript = """
        async ({data, video: isVideo}) => {
          const inspect = async () => {
            const media = document.createElement(isVideo ? 'video' : 'img');
            media.muted = true;
            const ready = new Promise((ok, no) => {
              if (isVideo) media.onloadeddata = ok; else media.onload = ok;
              media.onerror = () => no(new Error('CAPTURE_DECODE_FAILED'));
            });
            media.src = 'data:' + (isVideo ? 'video/webm' : 'image/jpeg') + ';base64,' + data;
            await ready;
            const duration = isVideo ? media.duration : 0;
            if (!Number.isFinite(duration) || duration > 10) throw new Error('CAPTURE_DURATION_INVALID');
            const canvas = document.createElement('canvas');
            canvas.width = isVideo ? media.videoWidth : media.naturalWidth;
            canvas.height = isVideo ? media.videoHeight : media.naturalHeight;
            if (!canvas.width || !canvas.height || canvas.width > 1280 || canvas.height > 720)
              throw new Error('CAPTURE_DIMENSIONS_INVALID');
            const ctx = canvas.getContext('2d', {willReadFrequently:true});
            const count = isVideo ? Math.ceil(duration * 25 - 0.00001) : 1;
            if (count < 1 || count > 250) throw new Error('CAPTURE_FRAME_COUNT_INVALID');
            let red = 0, firstRed = 0, lastRed = 0;
            for (let frame = 0; frame < count; frame++) {
              if (isVideo) {
                const seek = new Promise(ok => media.onseeked = ok);
                const painted = new Promise(ok => media.requestVideoFrameCallback((_, metadata) => ok(metadata)));
                media.currentTime = frame / 25 + 0.00001; await seek;
                const metadata = await painted;
                if (Math.abs(metadata.mediaTime - frame / 25) > 0.001)
                  throw new Error('CAPTURE_FRAME_POSITION_MISMATCH');
              }
              ctx.drawImage(media, 0, 0);
              const pixels = ctx.getImageData(0,0,canvas.width,canvas.height).data;
              let frameRed = 0;
              for (let i=0; i<pixels.length; i+=4) {
                if (pixels[i+3] !== 255) throw new Error('CAPTURE_FRAME_NOT_DECODED');
                if (pixels[i]>160 && pixels[i+1]<100 && pixels[i+2]<140) frameRed++;
              }
              red += frameRed;
              if (frame === 0) firstRed = frameRed;
              lastRed = frameRed;
            }
            media.removeAttribute('src');
            if (isVideo) media.load();
            return {frames:count, red, duration, firstRed, lastRed};
          };
          let timer;
          try { return await Promise.race([inspect(),new Promise((_,no)=>timer=setTimeout(()=>no(new Error('CAPTURE_DECODE_TIMEOUT')),8000))]); }
          finally {clearTimeout(timer);}
        }
        """;
}
