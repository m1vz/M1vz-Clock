namespace M1vClock.App.Overlay;

public partial class OverlayPanel : System.Windows.Controls.UserControl
{
    public OverlayPanel()
    {
        InitializeComponent();
    }

    public void SetScale(double scale)
    {
        Zoom.ScaleX = scale;
        Zoom.ScaleY = scale;
    }
}
