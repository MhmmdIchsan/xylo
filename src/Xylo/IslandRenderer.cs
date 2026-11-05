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

    public void Draw(SKCanvas canvas, float scale, float width, float height, bool detached)
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
        canvas.DrawCircle(left + 24, 25, 4, _paint);
        Text(canvas, "Xylo", left + 38, 30, 15, SKColors.White);
        Text(canvas, "Ready", left + width - 58, 29, 12, new SKColor(160, 169, 179));

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
