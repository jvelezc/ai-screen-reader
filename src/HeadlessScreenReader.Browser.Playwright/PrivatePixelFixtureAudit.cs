using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record PixelFixtureState(string FocusTag, int ValueLength, int Keys, int Pointers, int Clipboard);
public sealed record PixelFixtureColors(int Width, int Height, int Red, int Green, int Blue);

public sealed partial class ChromiumFixtureBrowser
{
    // Trusted proof instrumentation only. These fixed diagnostics are never exposed by the HTTP relay.
    private void EnsurePixelAudit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_pixelViewingEnabled || _fixture != BundledFixture.SyntheticHandoff || _publicPolicy is not null)
            throw new BrowserOperationException("PIXEL_VIEW_NOT_ENABLED");
        EnsureSyntheticDiagnosticsDisabled();
    }
    public async Task<PixelFixtureState> ReadPixelFixtureStateAsync()
    {
        EnsurePixelAudit();
        var state = await _page.EvaluateAsync<JsonElement>("""
            () => ({focus: document.activeElement.tagName, length: document.querySelector('input').value.length,
              ...window.viewerInputCounts})
            """).ConfigureAwait(false);
        return new(state.GetProperty("focus").GetString()!, state.GetProperty("length").GetInt32(),
            state.GetProperty("keys").GetInt32(), state.GetProperty("pointers").GetInt32(), state.GetProperty("clipboard").GetInt32());
    }
    public async Task<PixelFixtureColors> DecodePixelFixturePngAsync(ImmutableArray<byte> png)
    {
        EnsurePixelAudit();
        if (!ViewerPixelFrame.HasBoundedPngEnvelope(png)) throw new BrowserOperationException("PIXEL_FORMAT_UNCONFIRMED");
        var result = await _page.EvaluateAsync<JsonElement>("""
            async encoded => {
              const bytes=Uint8Array.from(atob(encoded), c=>c.charCodeAt(0));
              const bitmap=await createImageBitmap(new Blob([bytes],{type:'image/png'}));
              try {
                const canvas=new OffscreenCanvas(bitmap.width,bitmap.height),ctx=canvas.getContext('2d');ctx.drawImage(bitmap,0,0);
                const p=ctx.getImageData(0,0,bitmap.width,bitmap.height).data;let red=0,green=0,blue=0;
                for(let i=0;i<p.length;i+=4){if(p[i]>180&&p[i+1]<90&&p[i+2]<120)red++;
                  if(p[i]<90&&p[i+1]>140&&p[i+2]<120)green++;if(p[i]<90&&p[i+1]<140&&p[i+2]>170)blue++;}
                return {width:bitmap.width,height:bitmap.height,red,green,blue};
              } finally { bitmap.close(); }
            }
            """, Convert.ToBase64String(png.AsSpan())).ConfigureAwait(false);
        return new(result.GetProperty("width").GetInt32(), result.GetProperty("height").GetInt32(),
            result.GetProperty("red").GetInt32(), result.GetProperty("green").GetInt32(), result.GetProperty("blue").GetInt32());
    }
    /// <summary>Memory-only diagnostic positive control during the fixed synthetic gap; never a viewer delivery.</summary>
    public async Task<bool> ObservePrivatePixelPositiveControlAsync()
    {
        EnsurePixelAudit();
        if (CaptureStatus.Phase != CapturePhase.Excluded || !_syntheticCanaryMayRemain)
            throw new BrowserOperationException("PIXEL_POSITIVE_CONTROL_REQUIRES_GAP");
        var png = await _page.ScreenshotAsync(new() { Type = ScreenshotType.Png, Timeout = 5000 }).ConfigureAwait(false);
        return (await DecodePixelFixturePngAsync(ImmutableArray.CreateRange(png)).ConfigureAwait(false)).Red > 320 * 240 / 2;
    }
}
