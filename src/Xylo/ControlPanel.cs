namespace Xylo;

internal enum PanelPage { Home, Tasks, TaskDetails, Clipboard, Focus }
internal sealed record PanelRow(string Label, Action Invoke);
internal sealed record PanelView(string Title, IReadOnlyList<string> Rows, int Page, int PageCount, bool CanGoBack);
