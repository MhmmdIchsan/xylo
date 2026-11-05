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

    public void Draw(SKCanvas canvas, float scale, float width, float height, bool detached, BatteryStatus battery, double seconds, double blinkStarted, MediaSnapshot? media, LyricsSnapshot? lyrics, bool lyricsEnabled, FocusTimer focus, AgentApproval? approval, int pendingCount, AgentActivity? agent, NotificationPreview? preview, AgentNotice? notice, IReadOnlyList<TaskItem> tasks, int slide, TaskComposer? composer, ApprovalReview? review, InlinePrompt? prompt, PanelView? panel, WeatherSnapshot? weather, bool weatherEnabled, int unreadCount, bool clipboardEnabled, IReadOnlyList<string> clipboardItems)
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
        if (prompt != null)
            Text(canvas, "Attention", left + 52, 26, 12, SKColors.White);
        else if (review != null)
            Text(canvas, "Review request", left + 52, 26, 12, SKColors.White);
        else if (composer != null)
            Text(canvas, composer.EditIndex == null ? "New task" : "Edit task", left + 52, 26, 12, SKColors.White);
        else if (panel != null)
            Text(canvas, "Controls", left + 52, 26, 12, SKColors.White);
        else if (approval != null)
            Text(canvas, approval.Agent + " needs approval", left + 52, 26, 12, new SKColor(255, 185, 100));
        else if (notice != null)
            Text(canvas, notice.Agent + " needs approval", left + 52, 26, 12, new SKColor(255, 185, 100));
        else if (preview != null)
        {
            if (!preview.IsHud) NotificationIcon(canvas, left + 62, 20, preview.Kind);
            Text(canvas, Truncate(preview.Title, Math.Max(0, width - 155), 12), left + (preview.IsHud ? 52 : 78), 26, 12, SKColors.White);
        }
        else if (slide == 4 && media != null)
            Text(canvas, Truncate(media.Title, Math.Max(0, width - 155), 12), left + 52, 26, 12, new SKColor(235, 240, 242));
        else if (slide == 1 && agent != null)
            Text(canvas, Truncate(agent.Agent + (agent.Working ? " working" : " finished"), width - 125, 11), left + 52, 26, 11, new SKColor(130, 230, 190));
        else if (slide == 6 && focus.Active)
            Text(canvas, Truncate(focus.Display, width - 125, 12), left + 52, 26, 12, new SKColor(130, 230, 190));
        else
            Text(canvas, DateTime.Now.ToString("HH:mm"), left + 52, 26, 13, SKColors.White);
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
        if (unreadCount > 0 && height < 100 && preview == null && approval == null && notice == null)
            DrawBell(canvas, right - (battery.Percent.HasValue ? 82 : 24), 21);

        if (height > 200 && panel == null && composer == null && review == null && prompt == null && approval == null)
        {
            _paint.Color = new SKColor(180, 190, 195);
            for (int i = 0; i < 3; i++) canvas.DrawCircle(right - 91 + i * 6, 21, 1.7f, _paint);
        }

        if (approval != null && height < 110 && height > 54)
            Text(canvas, Truncate($"{approval.Tool} · click to review ({pendingCount})", width - 48, 12), left + 24, 52, 12, new SKColor(255, 185, 100));
        else if (notice != null && height < 110 && height > 54)
            Text(canvas, Truncate($"{notice.Tool} · review in {notice.Agent}", width - 48, 12), left + 24, 52, 12, new SKColor(255, 185, 100));
        else if (preview != null && height < 110 && height > 54)
        {
            Text(canvas, Truncate(preview.Body.Replace('\n', ' ').Replace('\r', ' '), width - 48, 12), left + 24, 52, 12, new SKColor(160, 169, 179));
            if (preview.Progress is float progress) DrawProgress(canvas, left + 24, 60, width - 48, progress);
        }
        else if (slide == 4 && media != null && height < 110 && height > 54)
        {
            string line = string.IsNullOrWhiteSpace(media.Artist) ? "Now playing" : media.Artist;
            CenterText(canvas, Truncate(line, width - 48, 12), left + width / 2, 52, 12, new SKColor(130, 230, 190));
        }

        float reveal = Math.Clamp((height - 55) / 165, 0, 1);
        reveal = reveal * reveal * (3 - 2 * reveal);
        if (reveal > 0)
        {
            _contentOpacity.Color = SKColors.White.WithAlpha((byte)(255 * reveal));
            canvas.SaveLayer(_contentOpacity);
            canvas.Translate(0, 8 * (1 - reveal));
            if (prompt != null)
            {
                Text(canvas, Truncate(prompt.Title, 352, 16), left + 24, 72, 16, SKColors.White);
                for (int i = 0; i < Math.Min(8, prompt.Lines.Length); i++)
                    Text(canvas, prompt.Lines[i], left + 24, 102 + 17 * i, 12, new SKColor(200, 210, 210));
                if (prompt.Confirm != null) DrawButton(canvas, left + 202, 240, 76, 42, "Cancel", false);
                DrawButton(canvas, left + 286, 240, 90, 42, prompt.ActionLabel, true, danger: prompt.Dangerous);
            }
            else if (review != null)
            {
                Text(canvas, "Review the complete request", left + 24, 67, 15, SKColors.White);
                Text(canvas, $"Page {review.Page + 1} / {review.PageCount}", left + 24, 87, 11, new SKColor(160, 169, 179));
                var lines = review.Lines.Skip(review.Page * ApprovalReview.LinesPerPage).Take(ApprovalReview.LinesPerPage);
                int row = 0;
                foreach (string line in lines) Text(canvas, line, left + 24, 107 + 16 * row++, 12, new SKColor(230, 235, 235));
                DrawButton(canvas, left + 24, 304, 74, 34, "Previous", false, review.Page > 0);
                DrawButton(canvas, left + 108, 304, 74, 34, "Next", false, review.Page < review.PageCount - 1);
                if (!review.CanApprove) Text(canvas, "Read all pages before allowing", left + 193, 325, 10, new SKColor(255, 185, 100));
                DrawButton(canvas, left + 24, 350, 100, 46, "Cancel", false);
                DrawButton(canvas, left + 140, 350, 100, 46, "Deny", false);
                DrawButton(canvas, left + 256, 350, 120, 46, "Allow", true, review.CanApprove);
            }
            else if (composer != null)
            {
                Text(canvas, "Task", left + 24, 70, 12, new SKColor(160, 169, 179));
                ComposerField(canvas, left + 24, 75, 352, composer.Title, !composer.EditingDue, composer);
                Text(canvas, "Due date and time (optional)", left + 24, 126, 11, new SKColor(160, 169, 179));
                DrawButton(canvas, left + 24, 132, 32, 34, "-", false);
                CenterText(canvas, composer.Due.Length >= 10 ? composer.Due[..10] : "No date", left + 144, 155, 13, SKColors.White);
                DrawButton(canvas, left + 232, 132, 32, 34, "+", false);
                DrawButton(canvas, left + 272, 132, 104, 34, "Clear date", false);
                DrawButton(canvas, left + 24, 170, 32, 34, "-", false);
                CenterText(canvas, composer.Due.Length > 10 ? composer.Due[11..13] : "--", left + 88, 193, 13, SKColors.White);
                DrawButton(canvas, left + 120, 170, 32, 34, "+", false);
                DrawButton(canvas, left + 176, 170, 32, 34, "-", false);
                CenterText(canvas, composer.Due.Length > 10 ? composer.Due[14..16] : "--", left + 236, 193, 13, SKColors.White);
                DrawButton(canvas, left + 264, 170, 32, 34, "+", false);
                DrawButton(canvas, left + 304, 170, 72, 34, "No time", false);
                Text(canvas, Truncate(composer.Error.Length > 0 ? composer.Error : "Hour                 Minute · 15 min steps", 352, 11), left + 24, 224, 11,
                    composer.Error.Length > 0 ? new SKColor(255, 142, 142) : new SKColor(160, 169, 179));
                DrawButton(canvas, left + 202, 240, 76, 42, "Cancel", false);
                DrawButton(canvas, left + 286, 240, 90, 42, "Save", true);
            }
            else if (panel != null)
            {
                Text(canvas, Truncate(panel.Title, 352, 16), left + 24, 69, 16, SKColors.White);
                for (int i = 0; i < panel.Rows.Count; i++)
                {
                    float rowY = 78 + i * 38;
                    _paint.Color = new SKColor(255, 255, 255, 13);
                    canvas.DrawRoundRect(new SKRect(left + 24, rowY, left + 376, rowY + 34), 8, 8, _paint);
                    Text(canvas, Truncate(panel.Rows[i], 328, 12), left + 36, rowY + 22, 12, new SKColor(235, 240, 240));
                }
                DrawButton(canvas, left + 24, 325, 76, 40, "Back", false, panel.CanGoBack);
                DrawButton(canvas, left + 108, 325, 66, 40, "Prev", false, panel.Page > 0);
                DrawButton(canvas, left + 180, 325, 66, 40, "Next", false, panel.Page + 1 < panel.PageCount);
                DrawButton(canvas, left + 260, 325, 116, 40, "Close", true);
            }
            else if (approval != null)
            {
                Text(canvas, approval.Agent + " needs your approval", left + 24, 72, 16, SKColors.White);
                Text(canvas, Truncate(approval.Tool, width - 48, 13), left + 24, 98, 13, new SKColor(255, 185, 100));
                Text(canvas, Truncate(approval.Project, width - 48, 12), left + 24, 122, 12, new SKColor(160, 169, 179));
                Text(canvas, "Click to review the complete request", left + 24, 160, 12, new SKColor(255, 185, 100));
                Text(canvas, $"{pendingCount} request(s) waiting", left + 24, 182, 12, new SKColor(160, 169, 179));
            }
            else if (notice != null)
            {
                Text(canvas, notice.Agent + " needs approval", left + 24, 72, 16, SKColors.White);
                Text(canvas, Truncate(notice.Tool, width - 48, 13), left + 24, 98, 13, new SKColor(255, 185, 100));
                Text(canvas, Truncate(notice.Project, width - 48, 12), left + 24, 122, 12, new SKColor(160, 169, 179));
                Text(canvas, "Review and respond in " + notice.Agent, left + 24, 160, 12, new SKColor(255, 185, 100));
            }
            else if (preview != null)
            {
                if (!preview.IsHud) NotificationIcon(canvas, left + 36, 67, preview.Kind);
                Text(canvas, Truncate(preview.Kind == "whatsapp-call" ? "WhatsApp call" : preview.Kind == "whatsapp" ? "WhatsApp" : preview.App, width - 76, 12), left + (preview.IsHud ? 24 : 54), 70, 12, new SKColor(130, 230, 190));
                Text(canvas, Truncate(preview.Title, width - 48, 16), left + 24, 99, 16, SKColors.White);
                Text(canvas, Truncate(preview.Body.Replace('\n', ' ').Replace('\r', ' '), width - 48, 12), left + 24, 128, 12, new SKColor(160, 169, 179));
                if (preview.Progress is float progress) DrawProgress(canvas, left + 24, 145, width - 48, progress);
            }
            else
            {
                string[] tabs = { "Today", "Agent", "Weather", "To-do", "Music", "Clip", "Focus" };
                for (int i = 0; i < tabs.Length; i++)
                {
                    float tabX = left + 24 + i * (352f / 7);
                    _paint.Color = i == slide ? new SKColor(130, 230, 190, 45) : new SKColor(255, 255, 255, 12);
                    canvas.DrawRoundRect(new SKRect(tabX, 45, tabX + 47, 81), 8, 8, _paint);
                    DrawTabIcon(canvas, tabX + 23, 55, i, i == slide);
                    CenterText(canvas, tabs[i], tabX + 23, 76, 9, i == slide ? new SKColor(130, 230, 190) : new SKColor(190, 200, 200));
                }
                if (slide == 0)
                {
                    var now = DateTime.Now;
                    Text(canvas, now.ToString("HH:mm"), left + 25, 135, 38, SKColors.White);
                    Text(canvas, Truncate(now.ToString("dddd, d MMMM yyyy"), 235, 13), left + 26, 159, 13, new SKColor(160, 180, 179));
                    _paint.Color = new SKColor(130, 230, 190, 28);
                    canvas.DrawRoundRect(new SKRect(left + 278, 95, left + 374, 196), 14, 14, _paint);
                    CenterText(canvas, now.ToString("MMM").ToUpperInvariant(), left + 326, 120, 12, new SKColor(130, 230, 190));
                    CenterText(canvas, now.ToString("dd"), left + 326, 164, 35, SKColors.White);
                    int mondayOffset = ((int)now.DayOfWeek + 6) % 7;
                    for (int i = 0; i < 7; i++)
                    {
                        DateTime day = now.Date.AddDays(i - mondayOffset);
                        float x = left + 40 + i * 31;
                        CenterText(canvas, day.ToString("ddd")[..1], x, 184, 10, new SKColor(160, 169, 179));
                        CenterText(canvas, day.Day.ToString(), x, 205, 12, i == mondayOffset ? new SKColor(130, 230, 190) : SKColors.White);
                    }
                }
                else if (slide == 1)
                {
                    Text(canvas, "AI Agent", left + 24, 105, 16, SKColors.White);
                    Text(canvas, agent == null ? "No recent agent activity" : Truncate(agent.Agent + (agent.Working ? " · working" : " · finished"), 352, 13), left + 24, 132, 13, new SKColor(130, 230, 190));
                    if (agent != null) Text(canvas, Truncate(agent.Project, 352, 12), left + 24, 155, 12, new SKColor(160, 169, 179));
                    Text(canvas, "Approval requests appear here automatically", left + 24, 204, 11, new SKColor(160, 169, 179));
                }
                else if (slide == 2)
                {
                    Text(canvas, "Weather", left + 24, 105, 16, SKColors.White);
                    Text(canvas, Truncate(weather != null ? $"{weather.City} · {weather.Summary}" : weatherEnabled ? "Loading or unavailable" : "Weather is off", 352, 13), left + 24, 135, 13, new SKColor(130, 230, 190));
                    if (weather != null)
                    {
                        for (int i = 0; i < weather.Forecast.Length; i++)
                        {
                            var hour = weather.Forecast[i];
                            float center = left + 60 + i * 88;
                            CenterText(canvas, hour.Time.ToString("HH:mm"), center, 165, 11, new SKColor(160, 169, 179));
                            CenterText(canvas, $"{Math.Round(hour.Temperature):0}°", center, 194, 17, SKColors.White);
                            DrawWeatherIcon(canvas, center, 212, hour.Code);
                        }
                    }
                    if (!weatherEnabled) DrawButton(canvas, left + 24, 175, 156, 40, "Enable weather", false);
                }
                else if (slide == 3)
                {
                    Text(canvas, $"To-do · {tasks.Count(t => t.Done)}/{tasks.Count} done", left + 24, 104, 15, SKColors.White);
                    var open = tasks.Where(t => !t.Done).Take(2).ToArray();
                    if (open.Length == 0) Text(canvas, "No open tasks", left + 24, 133, 12, new SKColor(160, 169, 179));
                    for (int i = 0; i < open.Length; i++)
                    {
                        _paint.Style = SKPaintStyle.Stroke; _paint.Color = new SKColor(130, 230, 190); _paint.StrokeWidth = 1.3f;
                        canvas.DrawCircle(left + 30, 128 + i * 22, 4, _paint); _paint.Style = SKPaintStyle.Fill;
                        Text(canvas, Truncate(open[i].Title, 330, 12), left + 42, 132 + i * 22, 12, new SKColor(220, 230, 230));
                    }
                    DrawButton(canvas, left + 24, 175, 156, 40, "Manage tasks", false);
                }
                else if (slide == 4)
                {
                    if (media == null) CenterText(canvas, "No media playing", left + width / 2, 140, 14, new SKColor(160, 169, 179));
                    else
                    {
                        CenterText(canvas, Truncate(media.Title + " · " + media.Artist, width - 48, 13), left + width / 2, 101, 13, SKColors.White);
                        string[] visible = lyrics?.Visible(media.CurrentPosition) ?? new[] { lyricsEnabled ? "Looking for lyrics…" : "Lyrics off" };
                        for (int i = 0; i < Math.Min(3, visible.Length); i++)
                            CenterText(canvas, Truncate(visible[i], width - 48, 12), left + width / 2, 126 + 17 * i, 12,
                                i == 0 ? new SKColor(130, 230, 190) : new SKColor(160, 169, 179));
                        DrawTransport(canvas, left + 145, 201, -1, false, true);
                        DrawTransport(canvas, left + 195, 201, 1, media.Playing, false);
                        DrawTransport(canvas, left + 245, 201, 1, false, true);
                    }
                    DrawButton(canvas, left + 290, 180, 86, 40, lyricsEnabled ? "Lyrics on" : "Lyrics off", false);
                }
                else if (slide == 5)
                {
                    Text(canvas, "Clipboard", left + 24, 104, 15, SKColors.White);
                    if (!clipboardEnabled) Text(canvas, "History is off. Enable to keep copied text in memory.", left + 24, 139, 11, new SKColor(160, 169, 179));
                    else if (clipboardItems.Count == 0) Text(canvas, "No copied text yet", left + 24, 139, 12, new SKColor(160, 169, 179));
                    else for (int i = 0; i < Math.Min(3, clipboardItems.Count); i++)
                        Text(canvas, Truncate(clipboardItems[i].Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), 340, 12), left + 30, 122 + i * 20, 12, new SKColor(220, 230, 230));
                    DrawButton(canvas, left + 24, 175, 156, 40, clipboardEnabled ? "Turn off" : "Enable history", false);
                    if (clipboardEnabled) DrawButton(canvas, left + 200, 175, 176, 40, "Clear history", false);
                }
                else
                {
                    Text(canvas, focus.Active ? $"{focus.Label}  {focus.Display}" : "Focus timer", left + 24, 104, 15, SKColors.White);
                    DrawButton(canvas, left + 24, 125, 104, 40, "Focus 25m", false);
                    DrawButton(canvas, left + 142, 125, 104, 40, "Break 5m", false);
                    DrawButton(canvas, left + 260, 125, 116, 40, "Break 15m", false);
                    DrawButton(canvas, left + 24, 175, 156, 40, focus.Running ? "Pause" : "Resume", false, focus.Active && !focus.Finished);
                    DrawButton(canvas, left + 200, 175, 176, 40, "Clear timer", false, focus.Active);
                }
            }
            canvas.Restore();
        }
        canvas.Restore();
    }

    private void DrawProgress(SKCanvas canvas, float x, float y, float width, float progress)
    {
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = new SKColor(255, 255, 255, 30);
        canvas.DrawRoundRect(new SKRect(x, y, x + width, y + 4), 2, 2, _paint);
        _paint.Color = new SKColor(130, 230, 190);
        canvas.DrawRoundRect(new SKRect(x, y, x + width * Math.Clamp(progress, 0, 1), y + 4), 2, 2, _paint);
    }

    private void ComposerField(SKCanvas canvas, float x, float y, float width, string value, bool selected, TaskComposer composer)
    {
        var rect = new SKRect(x, y, x + width, y + 34);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = new SKColor(255, 255, 255, 14);
        canvas.DrawRoundRect(rect, 8, 8, _paint);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1;
        _paint.Color = selected ? new SKColor(130, 230, 190, 210) : new SKColor(255, 255, 255, 35);
        canvas.DrawRoundRect(rect, 8, 8, _paint);
        _paint.Style = SKPaintStyle.Fill;
        _font.Size = 14;
        int caret = selected ? Math.Min(composer.Caret, value.Length) : value.Length;
        int start = 0;
        while (start < caret && _font.MeasureText(value[start..caret]) > width - 28) start++;
        canvas.Save();
        canvas.ClipRect(new SKRect(x + 8, y, x + width - 8, y + 34));
        Text(canvas, value[start..], x + 10, y + 23, 14, SKColors.White);
        if (selected && composer.Focused)
        {
            float caretX = x + 10 + _font.MeasureText(value[start..caret]);
            _paint.Color = new SKColor(130, 230, 190);
            _paint.StrokeWidth = 1.5f;
            canvas.DrawLine(caretX, y + 8, caretX, y + 26, _paint);
        }
        canvas.Restore();
    }

    internal string[] WrapReview(string text)
    {
        _font.Size = 12;
        var lines = new List<string>();
        foreach (string source in text.Split('\n'))
        {
            string current = "";
            foreach (char ch in source)
            {
                if (current.Length > 0 && _font.MeasureText(current + ch) > 352)
                { lines.Add(current); current = ""; }
                current += ch;
            }
            lines.Add(current);
        }
        return lines.ToArray();
    }

    private void DrawButton(SKCanvas canvas, float x, float y, float width, float height, string label, bool primary, bool enabled = true, bool danger = false)
    {
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = !enabled ? new SKColor(255, 255, 255, 10) : danger ? new SKColor(255, 150, 150) : primary ? new SKColor(130, 230, 190) : new SKColor(255, 255, 255, 22);
        canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), 10, 10, _paint);
        _font.Size = 13;
        Text(canvas, label, x + (width - _font.MeasureText(label)) / 2, y + 26, 13,
            !enabled ? new SKColor(100, 110, 110) : primary ? new SKColor(13, 25, 22) : SKColors.White);
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

    private void CenterText(SKCanvas canvas, string text, float x, float y, float size, SKColor color)
    {
        _font.Size = size;
        _paint.Color = color;
        canvas.DrawText(text, x, y, SKTextAlign.Center, _font, _paint);
    }

    private void DrawTabIcon(SKCanvas canvas, float x, float y, int icon, bool selected)
    {
        _paint.Color = selected ? new SKColor(130, 230, 190) : new SKColor(190, 200, 200);
        _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1.4f; _paint.StrokeCap = SKStrokeCap.Round;
        if (icon == 0) { canvas.DrawCircle(x, y, 5, _paint); canvas.DrawLine(x, y, x, y - 3, _paint); canvas.DrawLine(x, y, x + 3, y + 1, _paint); }
        else if (icon == 1) { canvas.DrawRoundRect(new SKRect(x - 5, y - 4, x + 5, y + 4), 2, 2, _paint); canvas.DrawLine(x, y - 4, x, y - 7, _paint); canvas.DrawPoint(x - 2, y, _paint); canvas.DrawPoint(x + 2, y, _paint); }
        else if (icon == 2) { canvas.DrawCircle(x, y, 3, _paint); canvas.DrawLine(x - 6, y, x - 5, y, _paint); canvas.DrawLine(x + 5, y, x + 6, y, _paint); canvas.DrawLine(x, y - 6, x, y - 5, _paint); canvas.DrawLine(x, y + 5, x, y + 6, _paint); }
        else if (icon == 3) { canvas.DrawRoundRect(new SKRect(x - 5, y - 5, x + 5, y + 5), 2, 2, _paint); canvas.DrawLine(x - 3, y, x - 1, y + 2, _paint); canvas.DrawLine(x - 1, y + 2, x + 3, y - 2, _paint); }
        else if (icon == 4) { canvas.DrawLine(x + 2, y - 5, x + 2, y + 3, _paint); canvas.DrawLine(x + 2, y - 5, x + 5, y - 6, _paint); canvas.DrawOval(new SKRect(x - 3, y + 1, x + 2, y + 4), _paint); }
        else if (icon == 5) { canvas.DrawRoundRect(new SKRect(x - 4, y - 5, x + 4, y + 5), 1, 1, _paint); canvas.DrawLine(x - 2, y - 6, x + 2, y - 6, _paint); canvas.DrawLine(x - 2, y - 1, x + 2, y - 1, _paint); canvas.DrawLine(x - 2, y + 2, x + 2, y + 2, _paint); }
        else { canvas.DrawCircle(x, y + 1, 5, _paint); canvas.DrawLine(x, y + 1, x + 2, y - 2, _paint); canvas.DrawLine(x - 2, y - 6, x + 2, y - 6, _paint); }
        _paint.Style = SKPaintStyle.Fill;
    }

    private void DrawWeatherIcon(SKCanvas canvas, float x, float y, int code)
    {
        DrawTabIcon(canvas, x, y, 2, true);
        if (code != 0)
        {
            _paint.Color = new SKColor(185, 210, 210); _paint.Style = SKPaintStyle.Fill;
            canvas.DrawCircle(x - 2, y + 1, 3, _paint); canvas.DrawCircle(x + 2, y + 1, 4, _paint);
            if (code is >= 51 and <= 82) { _paint.Color = new SKColor(100, 190, 245); canvas.DrawCircle(x, y + 6, 1.2f, _paint); }
        }
    }

    private void NotificationIcon(SKCanvas canvas, float x, float y, string kind)
    {
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = kind.StartsWith("whatsapp", StringComparison.Ordinal) ? new SKColor(37, 180, 105) : new SKColor(83, 108, 135);
        canvas.DrawCircle(x, y, 10, _paint);
        if (kind == "whatsapp-call")
        {
            _paint.Color = SKColors.White; _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 2;
            canvas.DrawArc(new SKRect(x - 4, y - 5, x + 4, y + 5), 45, 145, false, _paint);
            _paint.Style = SKPaintStyle.Fill;
        }
        else if (kind == "whatsapp") CenterText(canvas, "W", x, y + 4, 11, SKColors.White);
        else { _paint.Color = SKColors.White; canvas.DrawCircle(x, y, 2, _paint); }
    }

    private void DrawBell(SKCanvas canvas, float x, float y)
    {
        _paint.Color = new SKColor(160, 190, 185); _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1.4f;
        canvas.DrawArc(new SKRect(x - 5, y - 6, x + 5, y + 5), 180, 180, false, _paint);
        canvas.DrawLine(x - 5, y, x - 5, y + 4, _paint); canvas.DrawLine(x + 5, y, x + 5, y + 4, _paint);
        canvas.DrawLine(x - 6, y + 4, x + 6, y + 4, _paint);
        _paint.Style = SKPaintStyle.Fill; canvas.DrawCircle(x, y + 6, 1, _paint);
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
