using System.Drawing;
using System.Windows.Forms;

namespace FrameHub.App.Helpers;

public sealed class FrameHubDarkColorTable : ProfessionalColorTable
{
    private static readonly Color DarkBackground = Color.FromArgb(15, 23, 42);   // #0F172A
    private static readonly Color DarkBorder = Color.FromArgb(51, 65, 85);       // #334155
    private static readonly Color BlueSelected = Color.FromArgb(37, 99, 235);    // #2563EB
    private static readonly Color BluePressed = Color.FromArgb(29, 78, 216);     // #1D4ED8

    public override Color ToolStripDropDownBackground => DarkBackground;
    public override Color MenuBorder => DarkBorder;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => BlueSelected;
    public override Color MenuItemSelectedGradientBegin => BlueSelected;
    public override Color MenuItemSelectedGradientEnd => BlueSelected;
    public override Color MenuItemPressedGradientBegin => BluePressed;
    public override Color MenuItemPressedGradientMiddle => BluePressed;
    public override Color MenuItemPressedGradientEnd => BluePressed;
    public override Color ImageMarginGradientBegin => DarkBackground;
    public override Color ImageMarginGradientMiddle => DarkBackground;
    public override Color ImageMarginGradientEnd => DarkBackground;
    public override Color ImageMarginRevealedGradientBegin => DarkBackground;
    public override Color ImageMarginRevealedGradientMiddle => DarkBackground;
    public override Color ImageMarginRevealedGradientEnd => DarkBackground;
    public override Color SeparatorDark => DarkBorder;
    public override Color SeparatorLight => Color.Transparent;
    public override Color CheckBackground => BlueSelected;
    public override Color CheckSelectedBackground => Color.FromArgb(59, 130, 246);
    public override Color CheckPressedBackground => BluePressed;
}

public sealed class FrameHubDarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color NormalTextColor = Color.FromArgb(241, 245, 249);  // #F1F5F9
    private static readonly Color MutedTextColor = Color.FromArgb(148, 163, 184);   // #94A3B8
    private static readonly Color SelectedTextColor = Color.FromArgb(255, 255, 255); // #FFFFFF
    private static readonly Color BorderColor = Color.FromArgb(51, 65, 85);        // #334155

    public FrameHubDarkMenuRenderer() : base(new FrameHubDarkColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item == null)
        {
            base.OnRenderItemText(e);
            return;
        }

        if (!e.Item.Enabled)
        {
            e.TextColor = MutedTextColor;
        }
        else if (e.Item.Selected)
        {
            e.TextColor = SelectedTextColor;
        }
        else
        {
            e.TextColor = NormalTextColor;
        }

        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item != null)
        {
            e.ArrowColor = e.Item.Selected ? SelectedTextColor : MutedTextColor;
        }
        base.OnRenderArrow(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(BorderColor, 1);
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Item == null) return;
        using var pen = new Pen(BorderColor, 1);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }
}
