using SkiaSharp;

namespace Xylo;

internal sealed class IslandRenderer : IDisposable
{
    private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly SKFont _font;
    private readonly SKPath _outline = new();
    private readonly SKPaint _contentOpacity = new();

    public IslandRenderer() => _font = new SKFont(_typeface, 14);

    public void Draw(SKCanvas canvas, float scale, float width, float height, bool detached, BatteryStatus battery, double seconds, double blinkStarted)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(scale);
        float left = (IslandApp.CanvasWidth - width) / 2;
        float right = left + width;
        _outline.Reset();
        _outline.MoveTo(left, detached ? 23 : -1);
        if (detached)
        {
            _outline.QuadTo(left, 0, left + 23, 0);
            _outline.LineTo(right - 23, 0);
            _outline.QuadTo(right, 0, right, 23);
        }
        else _outline.LineTo(right, -1);
        _outline.LineTo(right, height - 23);
        _outline.QuadTo(right, height, right - 23, height);
        _outline.LineTo(left + 23, height);
        _outline.QuadTo(left, height, left, height - 23);
        _outline.Close();
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = new SKColor(14, 16, 20, 250);
        canvas.DrawPath(_outline, _paint);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1;
        _paint.Color = new SKColor(255, 255, 255, 28);
        canvas.DrawPath(_outline, _paint);
        _paint.Style = SKPaintStyle.Fill;
        canvas.ClipPath(_outline, SKClipOperation.Intersect, true);

        _paint.Color = new SKColor(130, 230, 190);
        float blink = blinkStarted < 0 ? 0 : (float)Math.Sin(Math.Clamp((seconds - blinkStarted) / 0.3, 0, 1) * Math.PI);
        float eyeHeight = 4.8f - blink * 4.0f;
        canvas.DrawRoundRect(new SKRect(left + 17, 21 - eyeHeight, left + 23, 21 + eyeHeight), 3, 3, _paint);
        canvas.DrawRoundRect(new SKRect(left + 26, 21 - eyeHeight, left + 32, 21 + eyeHeight), 3, 3, _paint);
        Text(canvas, "Xylo", left + 38, 30, 15, SKColors.White);
        if (battery.Percent is int percent)
        {
            float centerX = right - 23;
            var ring = new SKRect(centerX - 10, 11, centerX + 10, 31);
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeWidth = 2.5f;
            _paint.StrokeCap = SKStrokeCap.Round;
            _paint.Color = new SKColor(255, 255, 255, 35);
            canvas.DrawOval(ring, _paint);
            _paint.Color = percent <= 20 ? new SKColor(255, 113, 113) : new SKColor(130, 230, 190);
            canvas.DrawArc(ring, -90, Math.Max(0.01f, percent * 3.6f), false, _paint);
            if (battery.Charging)
            {
                _paint.Color = SKColors.White.WithAlpha(170);
                canvas.DrawArc(ring, (float)(seconds * 120 % 360) - 90, 28, false, _paint);
            }
            _paint.Style = SKPaintStyle.Fill;
            Text(canvas, $"{percent}%", centerX - 39, 26, 11, new SKColor(200, 209, 213));
            if (battery.Charging) Text(canvas, "+", centerX - 3.5f, 25, 11, SKColors.White);
        }
        else Text(canvas, "Ready", right - 58, 29, 12, new SKColor(160, 169, 179));

        float reveal = Math.Clamp((height - 55) / 165, 0, 1);
        reveal = reveal * reveal * (3 - 2 * reveal);
        if (reveal > 0)
        {
            _contentOpacity.Color = SKColors.White.WithAlpha((byte)(255 * reveal));
            canvas.SaveLayer(_contentOpacity);
            canvas.Translate(0, 8 * (1 - reveal));
            Text(canvas, "Your workspace, at a glance.", left + 24, 72, 17, SKColors.White);
            Text(canvas, "Desktop foundation is running.", left + 24, 97, 13, new SKColor(160, 169, 179));
            _paint.Color = new SKColor(255, 255, 255, 18);
            canvas.DrawLine(left + 24, 114, left + width - 24, 114, _paint);
            Text(canvas, "Agent activity", left + 24, 140, 13, SKColors.White);
            Text(canvas, "No integrations connected yet", left + 24, 161, 12, new SKColor(160, 169, 179));
            Text(canvas, "Hover to open · move away to close", left + 24, 198, 12, new SKColor(130, 230, 190));
            Text(canvas, "Right-click for menu", left + 24, 218, 11, new SKColor(160, 169, 179));
            canvas.Restore();
        }
        canvas.Restore();
    }

    private void Text(SKCanvas canvas, string text, float x, float y, float size, SKColor color)
    {
        _font.Size = size;
        _paint.Color = color;
        canvas.DrawText(text, x, y, SKTextAlign.Left, _font, _paint);
    }

    public void Dispose()
    {
        _font.Dispose();
        _outline.Dispose();
        _contentOpacity.Dispose();
        _paint.Dispose();
        _typeface.Dispose();
    }
}
