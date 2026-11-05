using System.Globalization;

namespace Xylo;

internal sealed class TaskComposer
{
    public int? EditIndex { get; }
    public bool OriginalDone { get; }
    public string Title { get; private set; }
    public string Due { get; private set; }
    public bool EditingDue { get; private set; }
    public bool Focused { get; set; }
    public int Caret { get; private set; }
    public string Error { get; private set; } = "";
    private bool _selectedAll;
    public string Text => EditingDue ? Due : Title;

    public TaskComposer(int? index, TaskItem? item)
    {
        EditIndex = index;
        OriginalDone = item?.Done ?? false;
        Title = item?.Title ?? "";
        Due = item?.Due?.ToString(item.HasTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        Caret = Title.Length;
    }

    public void SelectField(bool due)
    {
        EditingDue = due;
        Caret = Text.Length;
        _selectedAll = false;
        Error = "";
    }

    public void SelectAll() => _selectedAll = true;

    private DateTime SelectedDate() => DateTime.TryParseExact(Due, new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : DateTime.Today;
    public void ShiftDate(int days)
    {
        var date = SelectedDate().AddDays(days);
        Due = date.ToString(Due.Length > 10 ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Error = "";
    }
    public void ShiftTime(int minutes)
    {
        var date = SelectedDate().AddMinutes(minutes);
        Due = date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Error = "";
    }
    public void ClearDate() { Due = ""; Error = ""; }
    public void ClearTime() { if (Due.Length > 10) Due = Due[..10]; Error = ""; }

    public void Insert(string value)
    {
        value = new string(value.Where(c => !char.IsControl(c)).ToArray());
        string current = _selectedAll ? "" : Text;
        int cursor = _selectedAll ? 0 : Caret;
        int max = EditingDue ? 16 : 200;
        int count = Math.Min(value.Length, max - current.Length);
        if (count > 0 && count < value.Length && char.IsHighSurrogate(value[count - 1])) count--;
        if (count <= 0) return;
        SetText(current.Insert(cursor, value[..count]));
        Caret = cursor + count;
        _selectedAll = false;
        Error = "";
    }

    public void Backspace()
    {
        Error = "";
        if (_selectedAll) { SetText(""); Caret = 0; _selectedAll = false; return; }
        if (Caret == 0) return;
        int previous = Previous(Text, Caret);
        SetText(Text.Remove(previous, Caret - previous));
        Caret = previous;
    }

    public void Delete()
    {
        Error = "";
        if (_selectedAll) { SetText(""); Caret = 0; _selectedAll = false; return; }
        if (Caret >= Text.Length) return;
        SetText(Text.Remove(Caret, Next(Text, Caret) - Caret));
    }

    public void Move(int direction)
    {
        Caret = direction < 0 ? Previous(Text, Caret) : Next(Text, Caret);
        _selectedAll = false;
    }
    public void Home() { Caret = 0; _selectedAll = false; }
    public void End() { Caret = Text.Length; _selectedAll = false; }
    private void SetText(string text) { if (EditingDue) Due = text; else Title = text; }
    private static int Previous(string text, int index) => index <= 0 ? 0 : index >= 2 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2]) ? index - 2 : index - 1;
    private static int Next(string text, int index) => index >= text.Length ? text.Length : index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]) ? index + 2 : index + 1;

    public TaskItem? Build()
    {
        string title = Title.Trim();
        string dueText = Due.Trim();
        if (title.Length is 0 or > 200) { Error = "Enter a task title (up to 200 characters)."; return null; }
        DateTime? due = null;
        if (dueText.Length > 0)
        {
            if (!DateTime.TryParseExact(dueText, new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            { Error = "Use YYYY-MM-DD or YYYY-MM-DD HH:mm."; return null; }
            due = date;
        }
        return new TaskItem { Title = title, Due = due, HasTime = dueText.Length > 10, Done = OriginalDone };
    }
}
