using Godot;
using QRCoder;

namespace CouchCoopMod.CouchCoopModCode.QRCode;

public partial class QRCodeOverlay : CanvasLayer
{
    public void Setup(string url)
    {
        Layer = 100;

        var backdrop = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
            AnchorRight = 1,
            AnchorBottom = 1
        };

        var container = new CenterContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1
        };

        var panel = new PanelContainer();

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 32);
        margin.AddThemeConstantOverride("margin_bottom", 32);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 16);
        vbox.Alignment = BoxContainer.AlignmentMode.Center;

        var title = new Label
        {
            Text = "Couch Co-op",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 28);

        var qrRect = new TextureRect
        {
            Texture = GenerateQRTexture(url),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(256, 256)
        };

        var urlLabel = new Label
        {
            Text = url,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var hint = new Label
        {
            Text = "Scan to connect  |  F9 to dismiss",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));

        vbox.AddChild(title);
        vbox.AddChild(qrRect);
        vbox.AddChild(urlLabel);
        vbox.AddChild(hint);
        margin.AddChild(vbox);
        panel.AddChild(margin);
        container.AddChild(panel);
        AddChild(backdrop);
        AddChild(container);
    }

    public void Toggle()
    {
        Visible = !Visible;
    }

    private static ImageTexture GenerateQRTexture(string url)
    {
        var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var pngQr = new PngByteQRCode(data);
        var pngBytes = pngQr.GetGraphic(20);

        var image = new Image();
        image.LoadPngFromBuffer(pngBytes);
        return ImageTexture.CreateFromImage(image);
    }
}
