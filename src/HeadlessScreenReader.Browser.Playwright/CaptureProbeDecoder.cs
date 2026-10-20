using System.Collections.Immutable;
using System.Text.Json;
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
            var image = await EvaluateImageAsync(page, bytes, png: false, markers: false).ConfigureAwait(false);
            var red = image.GetProperty("red").GetInt64();
            return new(1, red, 0, red, red);
        }
        var frames = await PlaywrightVideoFrames.DecodeAsync(bytes).ConfigureAwait(false);
        long total = 0, first = 0, last = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var image = await EvaluateImageAsync(page, frames[i], png: true, markers: false).ConfigureAwait(false);
            var red = image.GetProperty("red").GetInt64();
            if (i == 0) first = red;
            last = red;
            total += red;
        }
        return new(frames.Length, total, frames.Length / 25d, first, last);
    }

    public static async Task<ImmutableArray<VideoMarkerFrame>> InspectMarkersAsync(IPage page, byte[] bytes, bool video)
    {
        var frames = video ? await PlaywrightVideoFrames.DecodeAsync(bytes).ConfigureAwait(false) : ImmutableArray.Create<byte[]>(bytes);
        var markers = ImmutableArray.CreateBuilder<VideoMarkerFrame>();
        for (var i = 0; i < frames.Length; i++)
        {
            var result = await EvaluateImageAsync(page, frames[i], png: video, markers: true).ConfigureAwait(false);
            var marker = result.GetProperty("marker");
            markers.Add(new(i, i * 40, marker.ValueKind == JsonValueKind.Null ? null
                : new(marker.GetProperty("objective").GetInt32(), marker.GetProperty("tick").GetInt32())));
        }
        return markers.ToImmutable();
    }

    private static async Task<JsonElement> EvaluateImageAsync(IPage page, byte[] bytes, bool png, bool markers)
    {
        try
        {
            return await page.EvaluateAsync<JsonElement>(ImageScript,
                new { data = Convert.ToBase64String(bytes), mime = png ? "image/png" : "image/jpeg", markers })
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (PlaywrightException exception)
        {
            var code = new[] { "CAPTURE_DECODE_FAILED", "CAPTURE_DIMENSIONS_INVALID", "CAPTURE_MARKER_DIMENSIONS_INVALID", "CAPTURE_FRAME_NOT_DECODED", "CAPTURE_DECODE_TIMEOUT" }
                .FirstOrDefault(code => exception.Message.Contains(code, StringComparison.Ordinal));
            throw new BrowserOperationException(code ?? "CAPTURE_DECODE_FAILED");
        }
    }

    private const string ImageScript = """
        async ({data, mime, markers}) => {
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
            let marker=null;
            if(markers){
              if(canvas.width!==320||canvas.height!==240)throw new Error('CAPTURE_MARKER_DIMENSIONS_INVALID');
              const values=[0,0,0,0];let valid=true;
              for(let bit=0;bit<32;bit++){
                const pixel=pixels[(208*canvas.width+bit*8+4)*4];
                if(pixel>55&&pixel<200)valid=false;
                values[bit>>3]=values[bit>>3]*2+(pixel>=200?1:0);
              }
              if(valid&&values[2]===(values[0]^values[1])&&values[3]===165)marker={objective:values[0],tick:values[1]};
            }
            image.removeAttribute('src');return {red,marker};
          };
          let timer;
          try{return await Promise.race([inspect(),new Promise((_,no)=>timer=setTimeout(()=>no(new Error('CAPTURE_DECODE_TIMEOUT')),8000))]);}
          finally{clearTimeout(timer);}
        }
        """;
}
