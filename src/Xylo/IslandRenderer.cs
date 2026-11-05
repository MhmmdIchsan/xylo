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

    public void Draw(SKCanvas canvas, float scale, float width, float height, bool detached, BatteryStatus battery, double seconds, double blinkStarted, MediaSnapshot? media, LyricsSnapshot? lyrics)
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

        _paint.Color = new SKColor(70, 82, 89);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1;
        canvas.DrawRoundRect(new SKRect(left + 13, 10, left + 42, 32), 8, 8, _paint);
        _paint.Color = new SKColor(130, 230, 190);
        float blink = blinkStarted < 0 ? 0 : (float)Math.Sin(Math.Clamp((seconds - blinkStarted) / 0.3, 0, 1) * Math.PI);
        float eyeHeight = 4.0f - blink * 3.5f;
        _paint.StrokeWidth = 2;
        _paint.StrokeCap = SKStrokeCap.Round;
        foreach (float eyeX in new[] { left + 22, left + 33 })
        {
            canvas.DrawLine(eyeX - 3, 21 - eyeHeight, eyeX + 3, 21 + eyeHeight, _paint);
            canvas.DrawLine(eyeX + 3, 21 - eyeHeight, eyeX - 3, 21 + eyeHeight, _paint);
        }
        _paint.Style = SKPaintStyle.Fill;
        if (media != null)
            Text(canvas, Truncate(media.Title, Math.Max(0, width - 135), 12), left + 52, 26, 12, new SKColor(235, 240, 242));
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
        else if (media == null) Text(canvas, "Ready", right - 58, 29, 12, new SKColor(160, 169, 179));

        if (media != null && height < 110 && height > 54)
        {
            string line = lyrics?.Current(media.CurrentPosition) ?? "Looking for lyrics…";
            Text(canvas, Truncate(line, width - 48, 12), left + 24, 52, 12, new SKColor(130, 230, 190));
        }

        float reveal = Math.Clamp((height - 55) / 165, 0, 1);
        reveal = reveal * reveal * (3 - 2 * reveal);
        if (reveal > 0)
        {
            _contentOpacity.Color = SKColors.White.WithAlpha((byte)(255 * reveal));
            canvas.SaveLayer(_contentOpacity);
            canvas.Translate(0, 8 * (1 - reveal));
            if (media == null)
            {
            Text(canvas, "Your workspace, at a glance.", left + 24, 72, 17, SKColors.White);
            Text(canvas, "Desktop foundation is running.", left + 24, 97, 13, new SKColor(160, 169, 179));
            _paint.Color = new SKColor(255, 255, 255, 18);
            canvas.DrawLine(left + 24, 114, left + width - 24, 114, _paint);
            Text(canvas, "Agent activity", left + 24, 140, 13, SKColors.White);
            Text(canvas, "No integrations connected yet", left + 24, 161, 12, new SKColor(160, 169, 179));
            Text(canvas, "Hover to open · move away to close", left + 24, 198, 12, new SKColor(130, 230, 190));
            Text(canvas, "Right-click for menu", left + 24, 218, 11, new SKColor(160, 169, 179));
            }
            else
            {
                Text(canvas, Truncate(media.Title, width - 48, 17), left + 24, 72, 17, SKColors.White);
                Text(canvas, Truncate(media.Artist, width - 48, 13), left + 24, 97, 13, new SKColor(160, 169, 179));
                string elapsed = TimeSpan.FromSeconds(media.CurrentPosition).ToString(@"m\:ss");
                string total = TimeSpan.FromSeconds(Math.Max(0, media.Duration)).ToString(@"m\:ss");
                Text(canvas, $"{elapsed} / {total}", left + 24, 118, 11, new SKColor(160, 169, 179));
                string[] visible = lyrics?.Visible(media.CurrentPosition) ?? new[] { "Looking for lyrics…" };
                for (int i = 0; i < Math.Min(3, visible.Length); i++)
                    Text(canvas, Truncate(visible[i], width - 48, 12), left + 24, 140 + 17 * i, 12,
                        i == 0 ? new SKColor(130, 230, 190) : new SKColor(160, 169, 179));
                DrawTransport(canvas, left + 145, 201, -1, false, true);
                DrawTransport(canvas, left + 195, 201, 1, media.Playing, false);
                DrawTransport(canvas, left + 245, 201, 1, false, true);
            }
            canvas.Restore();
        }
        canvas.Restore();
    }

    private void DrawTransport(SKCanvas canvas, float x, float y, int direction, bool pause, bool skip)
    {
        _paint.Color = SKColors.White;
        _paint.Style = SKPaintStyle.Fill;
        if (pause)
        {
            canvas.DrawRoundRect(new SKRect(x - 5, y - 7, x - 1, y + 7), 1, 1, _paint);
            canvas.DrawRoundRect(new SKRect(x + 1, y - 7, x + 5, y + 7), 1, 1, _paint);
            return;
        }
        using var path = new SKPath();
        path.MoveTo(x + direction * 6, y);
        path.LineTo(x - direction * 5, y - 7);
        path.LineTo(x - direction * 5, y + 7);
        path.Close();
        canvas.DrawPath(path, _paint);
        if (skip)
            canvas.DrawRect(new SKRect(x + direction * 7 - 1, y - 7, x + direction * 7 + 1, y + 7), _paint);
    }

    private string Truncate(string value, float maxWidth, float size)
    {
        _font.Size = size;
        if (_font.MeasureText(value) <= maxWidth) return value;
        while (value.Length > 0 && _font.MeasureText(value + "…") > maxWidth) value = value[..^1];
        return value.Length == 0 ? "" : value + "…";
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
