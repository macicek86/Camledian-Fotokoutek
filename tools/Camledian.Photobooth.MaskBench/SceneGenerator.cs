using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.MaskBench;

/// <summary>
/// Builds scenes whose ground truth is known by construction: take a background photo, paste a
/// subject and some props onto it, and remember exactly which pixels were added. Then degrade the
/// result the way a cheap webcam degrades a picture, so the numbers say something about the camera
/// this booth actually runs on rather than about a studio.
///
/// The subject is a cut-out PNG (a real person on transparency) when one is supplied, and a drawn
/// stand-in otherwise, so the tool works with nothing but what is in the repository.
/// </summary>
public static class SceneGenerator
{
    public static void Generate(string outputRoot, string backgroundPath, string? subjectCutoutPath, double subjectCoverage)
    {
        Directory.CreateDirectory(outputRoot);

        using var background = Image.Load<Rgba32>(backgroundPath);
        background.Mutate(ctx => ctx.Resize(1280, 853));

        // A room lit for a party, not a blown-out studio: leave headroom so a +25 % exposure step has
        // somewhere to go instead of clipping to white, which no algorithm can undo.
        background.Mutate(ctx => ctx.Brightness(0.62f));

        using var subject = LoadSubject(subjectCutoutPath, background.Width, background.Height, subjectCoverage);

        using var frame = background.Clone();
        var truth = new bool[frame.Width * frame.Height];

        var subjectOffset = new Point((frame.Width - subject.Width) / 2, frame.Height - subject.Height);
        frame.Mutate(ctx => ctx.DrawImage(subject, subjectOffset, 1f));
        MarkOpaquePixels(subject, subjectOffset, truth, frame.Width, frame.Height);

        // One prop in a colour nothing else in the scene has, one deliberately close to the room's own
        // colours — the second is the hard case for any difference-based method.
        var props = new List<(string Name, Rectangle Area, Rgba32 Colour)>
        {
            ("cedule-vyrazna", new Rectangle(980, 300, 140, 120), new Rgba32(200, 40, 40)),
            ("cedule-v-barve-pozadi", new Rectangle(900, 470, 110, 90), AverageColour(background, new Rectangle(900, 300, 100, 60))),
        };

        foreach (var (_, area, colour) in props)
        {
            frame.Mutate(ctx => ctx.Fill(Color.FromRgb(colour.R, colour.G, colour.B), area));
            for (var y = area.Top; y < area.Bottom; y++)
            {
                for (var x = area.Left; x < area.Right; x++)
                {
                    truth[(y * frame.Width) + x] = true;
                }
            }
        }

        background.SaveAsPng(Path.Combine(outputRoot, "reference.png"));
        SaveTruth(truth, frame.Width, frame.Height, Path.Combine(outputRoot, "truth.png"));
        File.WriteAllLines(
            Path.Combine(outputRoot, "props.txt"),
            props.Select(p => $"{p.Name} {p.Area.X} {p.Area.Y} {p.Area.Width} {p.Area.Height}"));

        // The three cases that actually go wrong on a laptop webcam.
        Degrade(frame, gain: 1.00, warmth: 1.00, noise: 0, quality: 95, Path.Combine(outputRoot, "frame-cisty.jpg"));
        Degrade(frame, gain: 1.25, warmth: 1.00, noise: 3, quality: 80, Path.Combine(outputRoot, "frame-preexponovano.jpg"));
        Degrade(frame, gain: 1.10, warmth: 1.06, noise: 6, quality: 70, Path.Combine(outputRoot, "frame-webkamera.jpg"));

        Console.WriteLine($"Scéna vygenerována do {outputRoot} (subjekt zabírá {truth.Count(t => t) * 100.0 / truth.Length:0} % plochy).");
    }

    private static Image<Rgba32> LoadSubject(string? cutoutPath, int sceneWidth, int sceneHeight, double coverage)
    {
        if (cutoutPath is not null && File.Exists(cutoutPath))
        {
            var cutout = Image.Load<Rgba32>(cutoutPath);
            var scale = Math.Sqrt(coverage * sceneWidth * sceneHeight / (cutout.Width * (double)cutout.Height));
            cutout.Mutate(ctx => ctx.Resize((int)(cutout.Width * scale), (int)(cutout.Height * scale)));
            return cutout;
        }

        // Stand-in: a head and shoulders in a skin tone, opaque where the "person" is. Crude, but the
        // measurements that matter — exposure drift, holes, props — do not depend on hair detail.
        var height = (int)(sceneHeight * Math.Sqrt(coverage) * 1.4);
        var width = (int)(height * 0.62);
        var subject = new Image<Rgba32>(width, height);
        subject.Mutate(ctx =>
        {
            ctx.Fill(Color.Transparent);
            ctx.Fill(Color.FromRgb(222, 184, 155), new SixLabors.ImageSharp.Drawing.EllipsePolygon(width / 2f, height * 0.22f, width * 0.26f, height * 0.2f));
            ctx.Fill(Color.FromRgb(60, 80, 120), new SixLabors.ImageSharp.Drawing.RectangularPolygon(width * 0.1f, height * 0.4f, width * 0.8f, height * 0.6f));
        });
        return subject;
    }

    private static void MarkOpaquePixels(Image<Rgba32> subject, Point offset, bool[] truth, int width, int height)
    {
        subject.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < subject.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var targetY = y + offset.Y;
                if (targetY < 0 || targetY >= height)
                {
                    continue;
                }

                for (var x = 0; x < subject.Width; x++)
                {
                    var targetX = x + offset.X;
                    if (targetX >= 0 && targetX < width && row[x].A > 127)
                    {
                        truth[(targetY * width) + targetX] = true;
                    }
                }
            }
        });
    }

    private static Rgba32 AverageColour(Image<Rgba32> image, Rectangle area)
    {
        long r = 0, g = 0, b = 0, count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = area.Top; y < Math.Min(area.Bottom, image.Height); y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = area.Left; x < Math.Min(area.Right, image.Width); x++)
                {
                    r += row[x].R;
                    g += row[x].G;
                    b += row[x].B;
                    count++;
                }
            }
        });

        return count == 0 ? new Rgba32(128, 128, 128) : new Rgba32((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    /// <param name="gain">Overall exposure change, i.e. the camera re-metering when a guest steps in.</param>
    /// <param name="warmth">Red/blue tilt, i.e. auto white balance drifting.</param>
    /// <param name="noise">Standard deviation of the sensor noise, in levels.</param>
    private static void Degrade(Image<Rgba32> source, double gain, double warmth, double noise, int quality, string path)
    {
        // Fixed seed: two runs of the tool have to be comparable, otherwise a 1-point difference in
        // the results could just be a different draw of noise.
        var random = new Random(7);
        using var output = source.Clone();
        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < output.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < output.Width; x++)
                {
                    ref var px = ref row[x];
                    px.R = Clamp((px.R * gain * warmth) + Noise(random, noise));
                    px.G = Clamp((px.G * gain) + Noise(random, noise));
                    px.B = Clamp((px.B * gain / warmth) + Noise(random, noise));
                }
            }
        });

        using var stream = File.Create(path);
        output.SaveAsJpeg(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = quality });
    }

    private static double Noise(Random random, double sigma)
    {
        if (sigma <= 0)
        {
            return 0;
        }

        // Box-Muller: ImageSharp gives us no Gaussian, and uniform noise would not behave like a sensor.
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2) * sigma;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static void SaveTruth(bool[] truth, int width, int height, string path)
    {
        using var image = new Image<L8>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    row[x] = new L8(truth[(y * width) + x] ? (byte)255 : (byte)0);
                }
            }
        });
        image.SaveAsPng(path);
    }
}
