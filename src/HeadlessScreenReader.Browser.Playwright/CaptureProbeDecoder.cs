using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record ProbePixels(int Frames, long CanaryPixels, double DurationSeconds,
    long FirstFrameCanaryPixels, long LastFrameCanaryPixels);

/// <summary>Decode original video natively; use Chromium only for independent still-image pixels.</summary>
internal static class CaptureProbeDecoder
{
    public static async Task<ProbePixels> InspectAsync(IPage page, byte[] bytes, bool video)
    {
        if (bytes.Length is 0 or > 16 * 1024 * 1024) throw new BrowserOperationException("CAPTURE_MEDIA_SIZE_INVALID");
        if (!video)
        {
            var red = await InspectImageAsync(page, bytes, png: false).ConfigureAwait(false);
            return new(1, red, 0, red, red);
        }
        var frames = await PlaywrightVideoFrames.DecodeAsync(bytes).ConfigureAwait(false);
        long total = 0, first = 0, last = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var red = await InspectImageAsync(page, frames[i], png: true).ConfigureAwait(false);
            if (i == 0) first = red;
            last = red;
            total += red;
        }
        return new(frames.Length, total, frames.Length / 25d, first, last);
    }

    private static async Task<long> InspectImageAsync(IPage page, byte[] bytes, bool png)
    {
        try
        {
            return await page.EvaluateAsync<long>(ImageScript,
                new { data = Convert.ToBase64String(bytes), mime = png ? "image/png" : "image/jpeg" })
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (PlaywrightException exception)
        {
            var code = new[] { "CAPTURE_DECODE_FAILED", "CAPTURE_DIMENSIONS_INVALID", "CAPTURE_FRAME_NOT_DECODED", "CAPTURE_DECODE_TIMEOUT" }
                .FirstOrDefault(code => exception.Message.Contains(code, StringComparison.Ordinal));
            throw new BrowserOperationException(code ?? "CAPTURE_DECODE_FAILED");
        }
    }

    private const string ImageScript = """
        async ({data, mime}) => {
          const inspect = async () => {
            const image = new Image();
            const ready = new Promise((ok,no)=>{image.onload=ok;image.onerror=()=>no(new Error('CAPTURE_DECODE_FAILED'));});
            image.src='data:'+mime+';base64,'+data; await ready;
            if (!image.naturalWidth || !image.naturalHeight || image.naturalWidth>1280 || image.naturalHeight>720)
              throw new Error('CAPTURE_DIMENSIONS_INVALID');
            const canvas=document.createElement('canvas');canvas.width=image.naturalWidth;canvas.height=image.naturalHeight;
            const ctx=canvas.getContext('2d',{willReadFrequently:true});ctx.drawImage(image,0,0);
            const pixels=ctx.getImageData(0,0,canvas.width,canvas.height).data;
            let red=0;
            for(let i=0;i<pixels.length;i+=4){
              if(pixels[i+3]!==255)throw new Error('CAPTURE_FRAME_NOT_DECODED');
              if(pixels[i]>160&&pixels[i+1]<100&&pixels[i+2]<140)red++;
            }
            image.removeAttribute('src');return red;
          };
          let timer;
          try{return await Promise.race([inspect(),new Promise((_,no)=>timer=setTimeout(()=>no(new Error('CAPTURE_DECODE_TIMEOUT')),8000))]);}
          finally{clearTimeout(timer);}
        }
        """;
}
