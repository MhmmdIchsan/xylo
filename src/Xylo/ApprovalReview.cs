using System.Text;

namespace Xylo;

internal sealed class ApprovalReview
{
    public const int LinesPerPage = 12;
    public AgentApproval Request { get; }
    public string[] Lines { get; }
    public int Page { get; private set; }
    private int _highestPage;
    public int PageCount => Math.Max(1, (Lines.Length + LinesPerPage - 1) / LinesPerPage);
    public bool CanApprove => _highestPage >= PageCount - 1;

    public ApprovalReview(AgentApproval request, Func<string, string[]> wrap)
    {
        Request = request;
        string details = $"Agent: {request.Agent}\nProject: {request.Project}\nTool: {request.Tool}\n\n{request.Details}";
        Lines = wrap(SafeDisplay(details));
    }

    // Escape invisible controls and non-ASCII so the exact request remains legible
    // even when the installed font cannot render a glyph or contains bidi controls.
    internal static string SafeDisplay(string input)
    {
        var result = new StringBuilder(input.Length);
        foreach (char ch in input.Replace("\r\n", "\n"))
        {
            if (ch == '\n') result.Append(ch);
            else if (ch < 32 || ch > 126) result.Append("\\u").Append(((int)ch).ToString("X4"));
            else result.Append(ch);
        }
        return result.ToString();
    }

    public void Next() { Page = Math.Min(Page + 1, PageCount - 1); _highestPage = Math.Max(_highestPage, Page); }
    public void Previous() => Page = Math.Max(0, Page - 1);
}
