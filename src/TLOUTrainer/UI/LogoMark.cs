namespace TLOUTrainer.UI;

internal static class LogoMark
{
    private static readonly Lazy<Image> Original = new(() =>
    {
        using var stream = typeof(LogoMark).Assembly.GetManifestResourceStream("TLOUP1.GameIcon")
            ?? throw new InvalidDataException("Oyun ikonu bulunamadı.");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    });

    public static void Paint(Graphics graphics, RectangleF bounds, bool backdrop = true) =>
        graphics.DrawImage(Original.Value, bounds);
}
