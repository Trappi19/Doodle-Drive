using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DoodleDrive.Views;

/// <summary>
/// Affiche un texte sur une ligne ; s'il dépasse, le survol de la souris déroule le texte
/// jusqu'au bout, marque une pause de 2 s, puis repart du début en boucle.
/// </summary>
public partial class MarqueeTextBlock : UserControl
{
    private const double SpeedPxPerSec = 80;
    private static readonly TimeSpan StartHold = TimeSpan.FromSeconds(0.6);
    private static readonly TimeSpan EndHold = TimeSpan.FromSeconds(2);

    public MarqueeTextBlock() => InitializeComponent();

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeTextBlock), new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        // Vraie largeur du texte, mesurée indépendamment du layout (fiable, pas de cache).
        var textWidth = MeasureTextWidth();
        var overflow = textWidth - ActualWidth;
        if (overflow <= 1) return; // tout est déjà visible
        overflow += 8; // petite marge pour bien voir la fin

        var scroll = TimeSpan.FromSeconds(overflow / SpeedPxPerSec);
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(StartHold)));                     // pause au début
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(StartHold + scroll)));    // défilement
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(StartHold + scroll + EndHold))); // pause à la fin
        Xform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        Xform.BeginAnimation(TranslateTransform.XProperty, null);
        Xform.X = 0;
    }

    private double MeasureTextWidth()
    {
        var typeface = new Typeface(Inner.FontFamily, Inner.FontStyle, Inner.FontWeight, Inner.FontStretch);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText(Inner.Text ?? string.Empty, CultureInfo.CurrentUICulture,
            Inner.FlowDirection, typeface, Inner.FontSize, Brushes.Black, dpi);
        return ft.WidthIncludingTrailingWhitespace;
    }
}
