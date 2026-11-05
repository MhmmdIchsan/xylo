namespace Xylo;

internal sealed record InlinePrompt(string Title, string[] Lines, string ActionLabel, Action? Confirm);
